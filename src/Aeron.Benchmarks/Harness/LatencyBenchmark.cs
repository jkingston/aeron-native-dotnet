using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Benchmarks.Config;
using Aeron.Native;
using Aeron.Native.Interop;
using HdrHistogram;

namespace Aeron.Benchmarks.Harness;

/// <summary>
/// LoadTestRig-style latency benchmark for the P/Invoke wrapper.
/// Measures round-trip latency using a ping/pong echo pattern.
///
/// Architecture:
///   Ping thread → Publication(channel, streamId) → Pong thread
///   Pong thread → Publication(channel, echoStreamId) → Ping thread
///
/// Each message carries a nanosecond timestamp. On receipt, the pong thread
/// echoes the message back. The ping thread records (now - timestamp) as RTT.
///
/// This runs both publisher and subscriber in the same process using IPC
/// for maximum isolation of the client library overhead (no network jitter).
/// </summary>
public sealed class LatencyBenchmark
{
    private readonly BenchmarkConfig _config;
    private readonly LongHistogram _histogram;

    // Shared state between ping thread and the unmanaged callback
    private static LongHistogram? s_activeHistogram;
    private static long s_messagesReceived;
    private static bool s_running;

    public LatencyBenchmark(BenchmarkConfig config)
    {
        _config = config;
        _histogram = new LongHistogram(config.HistogramHighestTrackableValue, config.HistogramSignificantDigits);
    }

    public LatencyResult Run()
    {
        Console.WriteLine($"[PInvoke Latency] channel={_config.Channel} msgLen={_config.MessageLength} " +
                          $"rate={_config.MessageRate}/s exclusive={_config.UseExclusivePublication} " +
                          $"tryClaim={_config.UseTryClaim}");

        using var ctx = new AeronContext()
            .ClientName("pinvoke-latency-benchmark");

        if (_config.UseSustainedLowLatencyGc)
        {
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        }

        using var client = AeronClient.Connect(ctx);

        // Create publications and subscriptions
        using var pingPub = _config.UseExclusivePublication
            ? client.AddExclusivePublication(_config.Channel, _config.StreamId)
            : client.AddPublication(_config.Channel, _config.StreamId);

        using var pongPub = _config.UseExclusivePublication
            ? client.AddExclusivePublication(_config.Channel, _config.EchoStreamId)
            : client.AddPublication(_config.Channel, _config.EchoStreamId);

        using var pingSub = client.AddSubscription(_config.Channel, _config.EchoStreamId);
        using var pongSub = client.AddSubscription(_config.Channel, _config.StreamId);

        // Wait for connections
        WaitForConnection(pingPub, "ping publication");
        WaitForConnection(pongPub, "pong publication");

        // Start pong (echo) thread
        s_running = true;
        var pongThread = new Thread(() => PongLoop(pongSub, pongPub))
        {
            IsBackground = true,
            Name = "pong-echo",
            Priority = ThreadPriority.Highest
        };
        pongThread.Start();

        // Warmup
        if (_config.WarmupIterations > 0)
        {
            Console.Write($"  Warming up ({_config.WarmupIterations} iterations)... ");
            RunIterations(pingPub, pingSub, _config.WarmupIterations, _config.WarmupMessageRate, discard: true);
            _histogram.Reset();
            Console.WriteLine("done");
        }

        // Measurement
        Console.Write($"  Measuring ({_config.Iterations} iterations)... ");

        long gcCountBefore = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        if (_config.ForceNoGcRegion)
        {
            GC.TryStartNoGCRegion(_config.NoGcRegionBytes);
        }

        var sw = Stopwatch.StartNew();
        RunIterations(pingPub, pingSub, _config.Iterations, _config.MessageRate, discard: false);
        sw.Stop();

        if (_config.ForceNoGcRegion && System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.NoGCRegion)
        {
            GC.EndNoGCRegion();
        }

        long gcCountAfter = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        Console.WriteLine("done");

        s_running = false;
        pongThread.Join(TimeSpan.FromSeconds(2));

        return new LatencyResult
        {
            Config = _config,
            Histogram = _histogram,
            ElapsedMs = sw.ElapsedMilliseconds,
            GcCollections = gcCountAfter - gcCountBefore,
        };
    }

    private unsafe void RunIterations(Publication pub, Subscription sub, int iterations, int messageRate, bool discard)
    {
        var histogram = discard ? new LongHistogram(_config.HistogramHighestTrackableValue, 3) : _histogram;
        s_activeHistogram = histogram;

        long sendIntervalNs = (long)(1_000_000_000.0 * _config.BatchSize / messageRate);
        var buffer = new byte[_config.MessageLength];

        for (int iter = 0; iter < iterations; iter++)
        {
            long sent = 0;
            s_messagesReceived = 0;
            long totalMessages = messageRate;

            long iterStartNs = Stopwatch.GetTimestamp();
            long nextSendNs = iterStartNs;

            while (sent < totalMessages)
            {
                long nowNs = Stopwatch.GetTimestamp();
                if (nowNs >= nextSendNs)
                {
                    for (int b = 0; b < _config.BatchSize && sent < totalMessages; b++)
                    {
                        // Embed send timestamp (Stopwatch ticks for high resolution)
                        long sendTimestamp = Stopwatch.GetTimestamp();
                        BitConverter.TryWriteBytes(buffer.AsSpan(0, 8), sendTimestamp);

                        long result;
                        if (_config.UseTryClaim)
                        {
                            result = TryClaimOffer(pub, buffer);
                        }
                        else
                        {
                            result = pub.Offer(buffer, 0, buffer.Length);
                        }

                        if (result > 0) sent++;
                        // On back-pressure or admin action, skip (matches LoadTestRig behavior)
                    }
                    nextSendNs += sendIntervalNs;
                }

                // Try to receive echoed messages
                sub.Poll(&OnPingReceived, 0, _config.FragmentLimit);
            }

            // Drain remaining responses
            long deadlineNs = Stopwatch.GetTimestamp() +
                              (long)_config.ReceiveDeadlineSeconds * Stopwatch.Frequency;
            while (Interlocked.Read(ref s_messagesReceived) < sent &&
                   Stopwatch.GetTimestamp() < deadlineNs)
            {
                sub.Poll(&OnPingReceived, 0, _config.FragmentLimit);
            }
        }
    }

    private unsafe long TryClaimOffer(Publication pub, byte[] buffer)
    {
        AeronBufferClaim claim;
        long position = pub.TryClaim(buffer.Length, &claim);
        if (position > 0)
        {
            fixed (byte* src = buffer)
            {
                Buffer.MemoryCopy(src, claim.Data, (long)claim.Length, buffer.Length);
            }
            AeronNative.BufferClaimCommit(&claim);
        }
        return position;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnPingReceived(nint clientd, byte* buffer, nuint length, nint header)
    {
        if (length >= 8)
        {
            long sendTimestamp = *(long*)buffer;
            long nowTicks = Stopwatch.GetTimestamp();
            long rttTicks = nowTicks - sendTimestamp;
            long rttNs = (long)(rttTicks * 1_000_000_000.0 / Stopwatch.Frequency);

            if (rttNs > 0 && rttNs < s_activeHistogram!.HighestTrackableValue)
            {
                s_activeHistogram.RecordValue(rttNs);
            }
        }
        Interlocked.Increment(ref s_messagesReceived);
    }

    /// <summary>
    /// Pong (echo) loop: reads messages from pongSub and echoes them back via pongPub.
    /// </summary>
    private unsafe void PongLoop(Subscription sub, Publication pub)
    {
        // We store the echo publication handle in a static so the callback can use it.
        s_echoPub = pub;

        while (s_running)
        {
            int fragments = sub.Poll(&OnPongReceived, 0, _config.FragmentLimit);
            if (fragments == 0)
            {
                Thread.SpinWait(1);
            }
        }
    }

    private static Publication? s_echoPub;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnPongReceived(nint clientd, byte* buffer, nuint length, nint header)
    {
        // Echo the message back unchanged (preserves timestamp)
        s_echoPub?.Offer(buffer, (int)length);
    }

    private static void WaitForConnection(Publication pub, string name)
    {
        var sw = Stopwatch.StartNew();
        while (!pub.IsConnected)
        {
            if (sw.ElapsedMilliseconds > 10_000)
                throw new TimeoutException($"Timed out waiting for {name} to connect");
            Thread.Yield();
        }
    }
}

public sealed class LatencyResult
{
    public required BenchmarkConfig Config { get; init; }
    public required LongHistogram Histogram { get; init; }
    public required long ElapsedMs { get; init; }
    public required long GcCollections { get; init; }

    public void PrintReport(string label)
    {
        Console.WriteLine();
        Console.WriteLine($"═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  {label}");
        Console.WriteLine($"═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Messages:     {Config.MessageRate * Config.Iterations:N0}");
        Console.WriteLine($"  Msg Length:   {Config.MessageLength} bytes");
        Console.WriteLine($"  Rate:         {Config.MessageRate:N0} msg/s");
        Console.WriteLine($"  TryClaim:     {Config.UseTryClaim}");
        Console.WriteLine($"  Exclusive:    {Config.UseExclusivePublication}");
        Console.WriteLine($"  Elapsed:      {ElapsedMs:N0} ms");
        Console.WriteLine($"  GC events:    {GcCollections}");
        Console.WriteLine($"───────────────────────────────────────────────────────────────");
        Console.WriteLine($"  RTT Latency (nanoseconds):");
        Console.WriteLine($"    Min:        {Histogram.GetValueAtPercentile(0):N0}");
        Console.WriteLine($"    p50:        {Histogram.GetValueAtPercentile(50):N0}");
        Console.WriteLine($"    p90:        {Histogram.GetValueAtPercentile(90):N0}");
        Console.WriteLine($"    p99:        {Histogram.GetValueAtPercentile(99):N0}");
        Console.WriteLine($"    p99.9:      {Histogram.GetValueAtPercentile(99.9):N0}");
        Console.WriteLine($"    p99.99:     {Histogram.GetValueAtPercentile(99.99):N0}");
        Console.WriteLine($"    Max:        {Histogram.GetValueAtPercentile(100):N0}");
        Console.WriteLine($"    Mean:       {Histogram.GetMean():N0}");
        Console.WriteLine($"    StdDev:     {Histogram.GetStdDeviation():N0}");
        Console.WriteLine($"    Count:      {Histogram.TotalCount:N0}");
        Console.WriteLine($"═══════════════════════════════════════════════════════════════");
    }
}
