using System.Diagnostics;
using Aeron.Benchmarks.Config;
using Adaptive.Aeron.LogBuffer;
using Adaptive.Agrona;
using Adaptive.Agrona.Concurrent;

namespace Aeron.Benchmarks.Harness;

/// <summary>
/// Throughput benchmark using Aeron.NET for comparison.
/// Same methodology as ThroughputBenchmark.
/// </summary>
public sealed class AeronNetThroughputBenchmark
{
    private readonly BenchmarkConfig _config;

    private volatile bool _running;
    private long _totalFragments;
    private long _totalBytes;

    public AeronNetThroughputBenchmark(BenchmarkConfig config)
    {
        _config = config;
    }

    public ThroughputResult Run()
    {
        Console.WriteLine($"[Aeron.NET Throughput] channel={_config.Channel} msgLen={_config.MessageLength} " +
                          $"exclusive={_config.UseExclusivePublication} tryClaim={_config.UseTryClaim}");

        if (_config.UseSustainedLowLatencyGc)
        {
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        }

        var ctx = new Adaptive.Aeron.Aeron.Context();
        using var aeron = Adaptive.Aeron.Aeron.Connect(ctx);

        Adaptive.Aeron.Publication pub = _config.UseExclusivePublication
            ? aeron.AddExclusivePublication(_config.Channel, _config.StreamId)
            : aeron.AddPublication(_config.Channel, _config.StreamId);

        using var sub = aeron.AddSubscription(_config.Channel, _config.StreamId);

        WaitForConnection(pub, "throughput publication");

        _running = true;
        _totalFragments = 0;
        _totalBytes = 0;

        // Subscriber thread
        var subThread = new Thread(SubscriberLoop)
        {
            IsBackground = true,
            Name = "aeron-net-throughput-sub",
            Priority = ThreadPriority.Highest
        };
        subThread.Start(sub);

        var sendBuffer = new UnsafeBuffer(new byte[_config.MessageLength]);
        var bufferClaim = new BufferClaim();

        int totalSeconds = _config.WarmupIterations + _config.Iterations;
        long[] throughputPerSecond = new long[_config.Iterations];
        long[] backPressurePerSecond = new long[_config.Iterations];

        Console.Write("  Running");

        for (int sec = 0; sec < totalSeconds; sec++)
        {
            long sentThisSecond = 0;
            long backPressureThisSecond = 0;
            long endTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency;

            while (Stopwatch.GetTimestamp() < endTicks)
            {
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

                if (result > 0)
                {
                    sentThisSecond++;
                }
                else if (result == Adaptive.Aeron.Publication.BACK_PRESSURED ||
                         result == Adaptive.Aeron.Publication.ADMIN_ACTION)
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

        _running = false;
        subThread.Join(TimeSpan.FromSeconds(2));

        pub.Dispose();

        return new ThroughputResult
        {
            Config = _config,
            ThroughputPerSecond = throughputPerSecond,
            BackPressurePerSecond = backPressurePerSecond,
            TotalFragmentsReceived = Interlocked.Read(ref _totalFragments),
            TotalBytesReceived = Interlocked.Read(ref _totalBytes),
        };
    }

    private void SubscriberLoop(object? state)
    {
        var sub = (Adaptive.Aeron.Subscription)state!;

        Adaptive.Aeron.LogBuffer.FragmentHandler handler = (buffer, offset, length, header) =>
        {
            Interlocked.Increment(ref _totalFragments);
            Interlocked.Add(ref _totalBytes, length);
        };

        while (_running)
        {
            int fragments = sub.Poll(handler, _config.FragmentLimit);
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
