using System.Diagnostics;
using Aeron.Benchmarks.Config;
using Aeron.Benchmarks.Harness;
using Aeron.Benchmarks.Micro;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using HdrHistogram;

namespace Aeron.Benchmarks;

/// <summary>
/// Aeron P/Invoke vs Aeron.NET benchmark runner.
///
/// Usage:
///   dotnet run -c Release -- latency [quick|hft|throughput]
///   dotnet run -c Release -- throughput [quick|hft|throughput]
///   dotnet run -c Release -- compare [quick|hft|throughput]
///   dotnet run -c Release -- micro
///   dotnet run -c Release -- all
///
/// Prerequisites:
///   - Aeron media driver must be running (Java or C driver)
///   - libaeron must be on the library path (for P/Invoke benchmarks)
///   - Aeron.Client NuGet package (for Aeron.NET benchmarks)
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        var profile = args.Length > 1 ? args[1].ToLowerInvariant() : "quick";

        var config = profile switch
        {
            "hft" => BenchmarkConfig.Hft(),
            "throughput" => BenchmarkConfig.Throughput(),
            "quick" => BenchmarkConfig.Quick(),
            _ => BenchmarkConfig.Quick()
        };

        Console.WriteLine($"Aeron Benchmark Suite");
        Console.WriteLine($"  .NET Runtime: {Environment.Version}");
        Console.WriteLine($"  OS: {Environment.OSVersion}");
        Console.WriteLine($"  Processors: {Environment.ProcessorCount}");
        Console.WriteLine($"  GC Server: {System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine($"  Stopwatch Freq: {Stopwatch.Frequency:N0} Hz ({(Stopwatch.IsHighResolution ? "high-res" : "low-res")})");
        Console.WriteLine();

        try
        {
            Console.WriteLine($"  Aeron C lib version: {Aeron.Native.AeronClient.Version}");
            Console.WriteLine($"  Default driver path: {Aeron.Native.AeronClient.DefaultPath}");
        }
        catch (DllNotFoundException ex)
        {
            Console.WriteLine($"  WARNING: libaeron not found ({ex.Message})");
            Console.WriteLine($"  P/Invoke benchmarks will be skipped.");
            Console.WriteLine($"  Build libaeron from aeron-c/ and add to LD_LIBRARY_PATH/DYLD_LIBRARY_PATH");
        }

        Console.WriteLine();

        return command switch
        {
            "latency" => RunLatency(config),
            "throughput" => RunThroughput(config),
            "compare" => RunCompare(config),
            "micro" => RunMicro(),
            "all" => RunAll(config),
            _ => PrintHelp()
        };
    }

    private static int RunLatency(BenchmarkConfig config)
    {
        var bench = new LatencyBenchmark(config);
        var result = bench.Run();
        result.PrintReport("P/Invoke Latency (RTT)");
        return 0;
    }

    private static int RunThroughput(BenchmarkConfig config)
    {
        var bench = new ThroughputBenchmark(config);
        var result = bench.Run();
        result.PrintReport("P/Invoke Throughput");
        return 0;
    }

    private static int RunCompare(BenchmarkConfig config)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        P/Invoke vs Aeron.NET Comparison Benchmark            ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // ── Latency comparison ──────────────────────────────────────────
        Console.WriteLine("─── LATENCY (Round-Trip) ───────────────────────────────────────");
        Console.WriteLine();

        LatencyResult? pinvokeLatency = null;
        LatencyResult? managedLatency = null;

        try
        {
            var pinvokeBench = new LatencyBenchmark(config);
            pinvokeLatency = pinvokeBench.Run();
            pinvokeLatency.PrintReport("P/Invoke Latency (RTT)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  P/Invoke latency SKIPPED: {ex.Message}");
        }

        Console.WriteLine();

        try
        {
            var managedBench = new AeronNetLatencyBenchmark(config);
            managedLatency = managedBench.Run();
            managedLatency.PrintReport("Aeron.NET Latency (RTT)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Aeron.NET latency SKIPPED: {ex.Message}");
        }

        // ── Throughput comparison ───────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("─── THROUGHPUT ─────────────────────────────────────────────────");
        Console.WriteLine();

        ThroughputResult? pinvokeThroughput = null;
        ThroughputResult? managedThroughput = null;

        try
        {
            var pinvokeTpBench = new ThroughputBenchmark(config);
            pinvokeThroughput = pinvokeTpBench.Run();
            pinvokeThroughput.PrintReport("P/Invoke Throughput");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  P/Invoke throughput SKIPPED: {ex.Message}");
        }

        Console.WriteLine();

        try
        {
            var managedTpBench = new AeronNetThroughputBenchmark(config);
            managedThroughput = managedTpBench.Run();
            managedThroughput.PrintReport("Aeron.NET Throughput");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Aeron.NET throughput SKIPPED: {ex.Message}");
        }

        // ── Summary comparison ──────────────────────────────────────────
        Console.WriteLine();
        PrintComparison(pinvokeLatency, managedLatency, pinvokeThroughput, managedThroughput);

        return 0;
    }

    private static void PrintComparison(
        LatencyResult? pinvokeLatency, LatencyResult? managedLatency,
        ThroughputResult? pinvokeTp, ThroughputResult? managedTp)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    COMPARISON SUMMARY                        ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");

        if (pinvokeLatency != null && managedLatency != null)
        {
            var ph = pinvokeLatency.Histogram;
            var mh = managedLatency.Histogram;

            Console.WriteLine("║  Latency (RTT, nanoseconds)                                 ║");
            Console.WriteLine("║                   P/Invoke      Aeron.NET      Delta         ║");
            Console.WriteLine("╟───────────────────────────────────────────────────────────────╢");
            PrintLatencyRow("p50",    ph.GetValueAtPercentile(50),    mh.GetValueAtPercentile(50));
            PrintLatencyRow("p99",    ph.GetValueAtPercentile(99),    mh.GetValueAtPercentile(99));
            PrintLatencyRow("p99.9",  ph.GetValueAtPercentile(99.9),  mh.GetValueAtPercentile(99.9));
            PrintLatencyRow("p99.99", ph.GetValueAtPercentile(99.99), mh.GetValueAtPercentile(99.99));
            PrintLatencyRow("Max",    ph.GetValueAtPercentile(100),   mh.GetValueAtPercentile(100));
            PrintLatencyRow("Mean",   (long)ph.GetMean(),             (long)mh.GetMean());

            Console.WriteLine($"║  GC events:   {pinvokeLatency.GcCollections,8}       {managedLatency.GcCollections,8}                        ║");
        }

        if (pinvokeTp != null && managedTp != null)
        {
            Console.WriteLine("╟───────────────────────────────────────────────────────────────╢");
            Console.WriteLine("║  Throughput (msg/s)                                          ║");
            Console.WriteLine("╟───────────────────────────────────────────────────────────────╢");
            double pAvg = pinvokeTp.ThroughputPerSecond.Average();
            double mAvg = managedTp.ThroughputPerSecond.Average();
            double delta = pAvg / mAvg;
            Console.WriteLine($"║  Avg:     {pAvg,12:N0}   {mAvg,12:N0}   {delta:F2}x             ║");
        }

        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    private static void PrintLatencyRow(string label, long pinvoke, long managed)
    {
        double ratio = managed > 0 ? (double)pinvoke / managed : 0;
        string delta = ratio < 1 ? $"{(1 - ratio) * 100:F1}% faster" : $"{(ratio - 1) * 100:F1}% slower";
        Console.WriteLine($"║  {label,-8}  {pinvoke,10:N0}   {managed,10:N0}   {delta,-16}  ║");
    }

    private static int RunMicro()
    {
        Console.WriteLine("Running BenchmarkDotNet microbenchmarks...");
        Console.WriteLine("(Requires running media driver for Offer/Poll benchmarks)");
        Console.WriteLine();

        // Use InProcess toolchain so DYLD_LIBRARY_PATH / LD_LIBRARY_PATH is inherited
        var inProcessConfig = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance))
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        BenchmarkRunner.Run<PInvokeOverheadBenchmarks>(inProcessConfig);

        Console.WriteLine();
        Console.WriteLine("Running Offer/Poll microbenchmarks (requires media driver)...");
        try
        {
            BenchmarkRunner.Run<OfferPollBenchmarks>(inProcessConfig);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Offer/Poll benchmarks failed: {ex.Message}");
        }

        return 0;
    }

    private static int RunAll(BenchmarkConfig config)
    {
        RunCompare(config);
        Console.WriteLine();
        RunMicro();
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("Aeron P/Invoke vs Aeron.NET Benchmark Suite");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -c Release -- <command> [profile]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  latency     Run P/Invoke latency benchmark (ping/pong RTT)");
        Console.WriteLine("  throughput  Run P/Invoke throughput benchmark (max msg/s)");
        Console.WriteLine("  compare     Run both P/Invoke and Aeron.NET side-by-side");
        Console.WriteLine("  micro       Run BenchmarkDotNet microbenchmarks");
        Console.WriteLine("  all         Run everything");
        Console.WriteLine();
        Console.WriteLine("Profiles:");
        Console.WriteLine("  quick       Low rate, few iterations (smoke test)");
        Console.WriteLine("  hft         HFT-style: 1M msg/s, 32-byte msgs, exclusive pub");
        Console.WriteLine("  throughput  Max throughput: 500K msg/s, 288-byte msgs, batch=10");
        Console.WriteLine();
        Console.WriteLine("Prerequisites:");
        Console.WriteLine("  1. Build libaeron: cd aeron-c && ./cppbuild/cppbuild");
        Console.WriteLine("  2. Set library path: export DYLD_LIBRARY_PATH=aeron-c/cppbuild/Release/lib");
        Console.WriteLine("  3. Start media driver (Java or C)");
        Console.WriteLine();
        Console.WriteLine("HFT Low-Latency Tips:");
        Console.WriteLine("  - Use 'hft' profile for realistic HFT workloads");
        Console.WriteLine("  - Set GC to Server mode: <ServerGarbageCollection>true</ServerGarbageCollection>");
        Console.WriteLine("  - Pin threads to cores: taskset/cpuset for Linux");
        Console.WriteLine("  - Use exclusive publications + tryClaim for lowest latency");
        Console.WriteLine("  - Consider NoGCRegion for ultra-critical paths");
        return 1;
    }
}
