namespace Aeron.Benchmarks.Config;

/// <summary>
/// Benchmark configuration matching the aeron-benchmarks LoadTestRig parameters.
/// </summary>
public sealed class BenchmarkConfig
{
    // ── Test parameters ─────────────────────────────────────────────────
    public int WarmupIterations { get; set; } = 10;
    public int WarmupMessageRate { get; set; } = 10_000;
    public int Iterations { get; set; } = 10;
    public int MessageRate { get; set; } = 100_000;
    public int BatchSize { get; set; } = 1;
    public int MessageLength { get; set; } = 32;
    public int FragmentLimit { get; set; } = 10;

    // ── Aeron configuration ─────────────────────────────────────────────
    public string Channel { get; set; } = "aeron:ipc";
    public int StreamId { get; set; } = 1001;
    public int EchoStreamId { get; set; } = 1002;
    public bool UseExclusivePublication { get; set; } = true;
    public bool UseTryClaim { get; set; } = true;

    // ── Timeout ─────────────────────────────────────────────────────────
    public int ReceiveDeadlineSeconds { get; set; } = 3;

    // ── GC control ──────────────────────────────────────────────────────
    public bool UseSustainedLowLatencyGc { get; set; } = true;
    public bool ForceNoGcRegion { get; set; } = false;
    public long NoGcRegionBytes { get; set; } = 256 * 1024 * 1024; // 256MB

    // ── Histogram ───────────────────────────────────────────────────────
    public long HistogramHighestTrackableValue { get; set; } = 3_600_000_000_000L; // 1 hour in ns
    public int HistogramSignificantDigits { get; set; } = 3;

    // ── Presets ─────────────────────────────────────────────────────────

    /// <summary>HFT-style: small messages, high rate, exclusive pub, tryClaim.</summary>
    public static BenchmarkConfig Hft() => new()
    {
        MessageLength = 32,
        MessageRate = 1_000_000,
        BatchSize = 1,
        UseExclusivePublication = true,
        UseTryClaim = true,
        UseSustainedLowLatencyGc = true,
        FragmentLimit = 1,
        Iterations = 20,
        WarmupIterations = 10,
    };

    /// <summary>Throughput-focused: larger messages, burst sending.</summary>
    public static BenchmarkConfig Throughput() => new()
    {
        MessageLength = 288,
        MessageRate = 500_000,
        BatchSize = 10,
        UseExclusivePublication = true,
        UseTryClaim = true,
        FragmentLimit = 10,
    };

    /// <summary>Quick smoke test.</summary>
    public static BenchmarkConfig Quick() => new()
    {
        MessageLength = 32,
        MessageRate = 10_000,
        BatchSize = 1,
        Iterations = 3,
        WarmupIterations = 2,
    };
}
