using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Benchmarks.Config;
using Aeron.Native;
using Aeron.Native.Interop;

namespace Aeron.Benchmarks.Harness;

/// <summary>
/// Throughput benchmark: measures maximum sustainable message rate.
/// Models the IPC throughput sample from Aeron.NET and the official benchmarks.
///
/// Publisher sends as fast as possible (busy-spin on back-pressure).
/// Subscriber counts received fragments. Throughput reported every second.
/// </summary>
public sealed class ThroughputBenchmark
{
    private readonly BenchmarkConfig _config;

    private static long s_totalFragments;
    private static long s_totalBytes;
    private static bool s_running;

    public ThroughputBenchmark(BenchmarkConfig config)
    {
        _config = config;
    }

    public ThroughputResult Run()
    {
        Console.WriteLine($"[PInvoke Throughput] channel={_config.Channel} msgLen={_config.MessageLength} " +
                          $"exclusive={_config.UseExclusivePublication} tryClaim={_config.UseTryClaim}");

        using var ctx = new AeronContext()
            .ClientName("pinvoke-throughput-benchmark");

        if (_config.UseSustainedLowLatencyGc)
        {
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        }

        using var client = AeronClient.Connect(ctx);

        using var pub = _config.UseExclusivePublication
            ? client.AddExclusivePublication(_config.Channel, _config.StreamId)
            : client.AddPublication(_config.Channel, _config.StreamId);

        using var sub = client.AddSubscription(_config.Channel, _config.StreamId);

        WaitForConnection(pub, "throughput publication");

        s_running = true;
        s_totalFragments = 0;
        s_totalBytes = 0;

        // Subscriber thread
        var subThread = new Thread(() => SubscriberLoop(sub))
        {
            IsBackground = true,
            Name = "throughput-subscriber",
            Priority = ThreadPriority.Highest
        };
        subThread.Start();

        // Publisher: send for (warmup + measurement) seconds
        int totalSeconds = _config.WarmupIterations + _config.Iterations;
        var buffer = new byte[_config.MessageLength];
        var rng = new Random(42);
        rng.NextBytes(buffer);

        long[] throughputPerSecond = new long[_config.Iterations];
        long[] backPressurePerSecond = new long[_config.Iterations];

        Console.Write("  Running");

        for (int sec = 0; sec < totalSeconds; sec++)
        {
            long sentThisSecond = 0;
            long backPressureThisSecond = 0;
            long startTicks = Stopwatch.GetTimestamp();
            long endTicks = startTicks + Stopwatch.Frequency; // 1 second

            while (Stopwatch.GetTimestamp() < endTicks)
            {
                long result;
                if (_config.UseTryClaim)
                {
                    result = TryClaimOffer(pub, buffer);
                }
                else
                {
                    result = pub.Offer(buffer, 0, buffer.Length);
                }

                if (result > 0)
                {
                    sentThisSecond++;
                }
                else if (result == Publication.BackPressured || result == Publication.AdminAction)
                {
                    backPressureThisSecond++;
                    Thread.SpinWait(1);
                }
            }

            if (sec >= _config.WarmupIterations)
            {
                int idx = sec - _config.WarmupIterations;
                throughputPerSecond[idx] = sentThisSecond;
                backPressurePerSecond[idx] = backPressureThisSecond;
            }

            Console.Write(sec < _config.WarmupIterations ? " W" : " .");
        }

        Console.WriteLine(" done");

        s_running = false;
        subThread.Join(TimeSpan.FromSeconds(2));

        return new ThroughputResult
        {
            Config = _config,
            ThroughputPerSecond = throughputPerSecond,
            BackPressurePerSecond = backPressurePerSecond,
            TotalFragmentsReceived = Interlocked.Read(ref s_totalFragments),
            TotalBytesReceived = Interlocked.Read(ref s_totalBytes),
        };
    }

    private static unsafe long TryClaimOffer(Publication pub, byte[] buffer)
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

    private unsafe void SubscriberLoop(Subscription sub)
    {
        while (s_running)
        {
            int fragments = sub.Poll(&OnFragment, 0, _config.FragmentLimit);
            if (fragments == 0) Thread.SpinWait(1);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnFragment(nint clientd, byte* buffer, nuint length, nint header)
    {
        Interlocked.Increment(ref s_totalFragments);
        Interlocked.Add(ref s_totalBytes, (long)length);
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

public sealed class ThroughputResult
{
    public required BenchmarkConfig Config { get; init; }
    public required long[] ThroughputPerSecond { get; init; }
    public required long[] BackPressurePerSecond { get; init; }
    public required long TotalFragmentsReceived { get; init; }
    public required long TotalBytesReceived { get; init; }

    public void PrintReport(string label)
    {
        double avgThroughput = ThroughputPerSecond.Average();
        double maxThroughput = ThroughputPerSecond.Max();
        double minThroughput = ThroughputPerSecond.Min();
        double avgBackPressure = BackPressurePerSecond.Average();

        Console.WriteLine();
        Console.WriteLine($"═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  {label}");
        Console.WriteLine($"═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Msg Length:     {Config.MessageLength} bytes");
        Console.WriteLine($"  TryClaim:       {Config.UseTryClaim}");
        Console.WriteLine($"  Exclusive:      {Config.UseExclusivePublication}");
        Console.WriteLine($"───────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Throughput (msg/s):");
        Console.WriteLine($"    Avg:          {avgThroughput:N0}");
        Console.WriteLine($"    Max:          {maxThroughput:N0}");
        Console.WriteLine($"    Min:          {minThroughput:N0}");
        Console.WriteLine($"    Avg BP/s:     {avgBackPressure:N0}");
        Console.WriteLine($"───────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Per-second breakdown:");
        for (int i = 0; i < ThroughputPerSecond.Length; i++)
        {
            Console.WriteLine($"    [{i + 1}] {ThroughputPerSecond[i]:N0} msg/s  (BP: {BackPressurePerSecond[i]:N0})");
        }
        Console.WriteLine($"───────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Total received: {TotalFragmentsReceived:N0} fragments, {TotalBytesReceived:N0} bytes");
        Console.WriteLine($"═══════════════════════════════════════════════════════════════");
    }
}
