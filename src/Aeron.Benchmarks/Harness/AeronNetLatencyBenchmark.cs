using System.Diagnostics;
using Aeron.Benchmarks.Config;
using Adaptive.Aeron;
using Adaptive.Aeron.LogBuffer;
using Adaptive.Agrona;
using Adaptive.Agrona.Concurrent;
using HdrHistogram;

namespace Aeron.Benchmarks.Harness;

/// <summary>
/// Latency benchmark using the pure .NET Aeron.NET client for comparison.
/// Same methodology as LatencyBenchmark (ping/pong echo pattern with HdrHistogram).
/// </summary>
public sealed class AeronNetLatencyBenchmark
{
    private readonly BenchmarkConfig _config;
    private readonly LongHistogram _histogram;

    private volatile bool _running;
    private long _messagesReceived;

    public AeronNetLatencyBenchmark(BenchmarkConfig config)
    {
        _config = config;
        _histogram = new LongHistogram(config.HistogramHighestTrackableValue, config.HistogramSignificantDigits);
    }

    public LatencyResult Run()
    {
        Console.WriteLine($"[Aeron.NET Latency] channel={_config.Channel} msgLen={_config.MessageLength} " +
                          $"rate={_config.MessageRate}/s exclusive={_config.UseExclusivePublication} " +
                          $"tryClaim={_config.UseTryClaim}");

        if (_config.UseSustainedLowLatencyGc)
        {
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        }

        var ctx = new Adaptive.Aeron.Aeron.Context();
        using var aeron = Adaptive.Aeron.Aeron.Connect(ctx);

        // Publications
        Adaptive.Aeron.Publication pingPub = _config.UseExclusivePublication
            ? aeron.AddExclusivePublication(_config.Channel, _config.StreamId)
            : aeron.AddPublication(_config.Channel, _config.StreamId);

        Adaptive.Aeron.Publication pongPub = _config.UseExclusivePublication
            ? aeron.AddExclusivePublication(_config.Channel, _config.EchoStreamId)
            : aeron.AddPublication(_config.Channel, _config.EchoStreamId);

        using var pingSub = aeron.AddSubscription(_config.Channel, _config.EchoStreamId);
        using var pongSub = aeron.AddSubscription(_config.Channel, _config.StreamId);

        // Wait for connections
        WaitForConnection(pingPub, "ping publication");
        WaitForConnection(pongPub, "pong publication");

        // Pong (echo) thread
        _running = true;
        var pongThread = new Thread(() => PongLoop(pongSub, pongPub))
        {
            IsBackground = true,
            Name = "aeron-net-pong",
            Priority = ThreadPriority.Highest
        };
        pongThread.Start();

        var sendBuffer = new UnsafeBuffer(new byte[_config.MessageLength]);
        var bufferClaim = new BufferClaim();

        // Warmup
        if (_config.WarmupIterations > 0)
        {
            Console.Write($"  Warming up ({_config.WarmupIterations} iterations)... ");
            RunIterations(pingPub, pingSub, sendBuffer, bufferClaim,
                _config.WarmupIterations, _config.WarmupMessageRate, discard: true);
            _histogram.Reset();
            Console.WriteLine("done");
        }

        // Measurement
        Console.Write($"  Measuring ({_config.Iterations} iterations)... ");

        long gcCountBefore = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        var sw = Stopwatch.StartNew();

        RunIterations(pingPub, pingSub, sendBuffer, bufferClaim,
            _config.Iterations, _config.MessageRate, discard: false);

        sw.Stop();
        long gcCountAfter = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        Console.WriteLine("done");

        _running = false;
        pongThread.Join(TimeSpan.FromSeconds(2));

        pingPub.Dispose();
        pongPub.Dispose();

        return new LatencyResult
        {
            Config = _config,
            Histogram = _histogram,
            ElapsedMs = sw.ElapsedMilliseconds,
            GcCollections = gcCountAfter - gcCountBefore,
        };
    }

    private void RunIterations(
        Adaptive.Aeron.Publication pub,
        Adaptive.Aeron.Subscription sub,
        UnsafeBuffer sendBuffer,
        BufferClaim bufferClaim,
        int iterations, int messageRate, bool discard)
    {
        var histogram = discard ? new LongHistogram(_config.HistogramHighestTrackableValue, 3) : _histogram;
        long sendIntervalNs = (long)(1_000_000_000.0 * _config.BatchSize / messageRate);

        Adaptive.Aeron.LogBuffer.FragmentHandler pingHandler = (buffer, offset, length, header) =>
        {
            if (length >= 8)
            {
                long sendTimestamp = buffer.GetLong(offset);
                long nowTicks = Stopwatch.GetTimestamp();
                long rttTicks = nowTicks - sendTimestamp;
                long rttNs = (long)(rttTicks * 1_000_000_000.0 / Stopwatch.Frequency);

                if (rttNs > 0 && rttNs < histogram.HighestTrackableValue)
                {
                    histogram.RecordValue(rttNs);
                }
            }
            Interlocked.Increment(ref _messagesReceived);
        };

        for (int iter = 0; iter < iterations; iter++)
        {
            long sent = 0;
            _messagesReceived = 0;
            long totalMessages = messageRate;

            long nextSendNs = Stopwatch.GetTimestamp();

            while (sent < totalMessages)
            {
                long nowNs = Stopwatch.GetTimestamp();
                if (nowNs >= nextSendNs)
                {
                    for (int b = 0; b < _config.BatchSize && sent < totalMessages; b++)
                    {
                        long sendTimestamp = Stopwatch.GetTimestamp();
                        sendBuffer.PutLong(0, sendTimestamp);

                        long result;
                        if (_config.UseTryClaim)
                        {
                            result = pub.TryClaim(_config.MessageLength, bufferClaim);
                            if (result > 0)
                            {
                                bufferClaim.Buffer.PutBytes(bufferClaim.Offset, sendBuffer, 0, _config.MessageLength);
                                bufferClaim.Commit();
                            }
                        }
                        else
                        {
                            result = pub.Offer(sendBuffer, 0, _config.MessageLength);
                        }

                        if (result > 0) sent++;
                    }
                    nextSendNs += sendIntervalNs;
                }

                sub.Poll(pingHandler, _config.FragmentLimit);
            }

            // Drain remaining
            long deadlineTicks = Stopwatch.GetTimestamp() +
                                 (long)_config.ReceiveDeadlineSeconds * Stopwatch.Frequency;
            while (Interlocked.Read(ref _messagesReceived) < sent &&
                   Stopwatch.GetTimestamp() < deadlineTicks)
            {
                sub.Poll(pingHandler, _config.FragmentLimit);
            }
        }
    }

    private void PongLoop(Adaptive.Aeron.Subscription sub, Adaptive.Aeron.Publication pub)
    {
        var echoBuffer = new UnsafeBuffer(new byte[_config.MessageLength]);

        Adaptive.Aeron.LogBuffer.FragmentHandler echoHandler = (buffer, offset, length, header) =>
        {
            buffer.GetBytes(offset, echoBuffer, 0, length);
            while (pub.Offer(echoBuffer, 0, length) < 0 && _running)
            {
                Thread.SpinWait(1);
            }
        };

        while (_running)
        {
            int fragments = sub.Poll(echoHandler, _config.FragmentLimit);
            if (fragments == 0) Thread.SpinWait(1);
        }
    }

    private static void WaitForConnection(Adaptive.Aeron.Publication pub, string name)
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
