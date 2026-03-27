using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Native;
using Aeron.Native.Interop;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace Aeron.Benchmarks.Micro;

/// <summary>
/// Microbenchmarks for isolated P/Invoke call overhead.
/// Measures the raw cost of crossing the managed/native boundary.
/// These run without a media driver - they test the client library call path only
/// where possible, or use mock patterns for hot-path operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 20)]
public class PInvokeOverheadBenchmarks
{
    /// <summary>
    /// Baseline: call aeron_nano_clock via SuppressGCTransition.
    /// This is the minimum P/Invoke overhead - a simple rdtsc-style call.
    /// </summary>
    [Benchmark(Baseline = true)]
    public long NanoClock_PInvoke()
    {
        return AeronNative.NanoClock();
    }

    /// <summary>
    /// Comparison: Stopwatch.GetTimestamp() (managed equivalent).
    /// </summary>
    [Benchmark]
    public long NanoClock_Managed()
    {
        return System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Measure aeron_version_full() - a simple string return P/Invoke without SuppressGCTransition.
    /// </summary>
    [Benchmark]
    public nint VersionFull_PInvoke()
    {
        return AeronNative.VersionFull();
    }

    /// <summary>
    /// Measure aeron_errcode() - trivial thread-local read.
    /// </summary>
    [Benchmark]
    public int ErrCode_PInvoke()
    {
        return AeronNative.ErrCode();
    }

    /// <summary>
    /// Measure aeron_epoch_clock() - another SuppressGCTransition call.
    /// </summary>
    [Benchmark]
    public long EpochClock_PInvoke()
    {
        return AeronNative.EpochClock();
    }
}

/// <summary>
/// Microbenchmarks for offer/poll operations. Requires a running media driver.
/// Skipped if driver not available.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class OfferPollBenchmarks
{
    private AeronContext? _nativeCtx;
    private AeronClient? _nativeClient;
    private Publication? _nativePub;
    private Subscription? _nativeSub;

    private Adaptive.Aeron.Aeron? _managedAeron;
    private Adaptive.Aeron.Publication? _managedPub;
    private Adaptive.Aeron.Subscription? _managedSub;

    private byte[] _buffer = null!;
    private Adaptive.Agrona.Concurrent.UnsafeBuffer _managedBuffer = null!;

    [Params(32, 128, 512)]
    public int MessageLength { get; set; }

    private bool _driverAvailable;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[MessageLength];
        new Random(42).NextBytes(_buffer);

        try
        {
            // Native P/Invoke client
            _nativeCtx = new AeronContext().ClientName("bench-native");
            _nativeClient = AeronClient.Connect(_nativeCtx);
            _nativePub = _nativeClient.AddExclusivePublication("aeron:ipc", 2001);
            _nativeSub = _nativeClient.AddSubscription("aeron:ipc", 2001);

            // Managed Aeron.NET client
            var managedCtx = new Adaptive.Aeron.Aeron.Context();
            _managedAeron = Adaptive.Aeron.Aeron.Connect(managedCtx);
            _managedPub = _managedAeron.AddExclusivePublication("aeron:ipc", 2002);
            _managedSub = _managedAeron.AddSubscription("aeron:ipc", 2002);

            _managedBuffer = new Adaptive.Agrona.Concurrent.UnsafeBuffer(_buffer);

            // Wait for connections
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while ((!_nativePub.IsConnected || !_managedPub.IsConnected) && sw.ElapsedMilliseconds < 5000)
                Thread.Yield();

            _driverAvailable = _nativePub.IsConnected && _managedPub.IsConnected;
        }
        catch
        {
            _driverAvailable = false;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nativeSub?.Dispose();
        _nativePub?.Dispose();
        _nativeClient?.Dispose();

        _managedSub?.Dispose();
        _managedPub?.Dispose();
        _managedAeron?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long Native_Offer()
    {
        if (!_driverAvailable) return 0;
        return _nativePub!.Offer(_buffer, 0, _buffer.Length);
    }

    [Benchmark]
    public long Managed_Offer()
    {
        if (!_driverAvailable) return 0;
        return _managedPub!.Offer(_managedBuffer, 0, MessageLength);
    }

    [Benchmark]
    public unsafe long Native_TryClaim()
    {
        if (!_driverAvailable) return 0;
        AeronBufferClaim claim;
        long pos = _nativePub!.TryClaim(MessageLength, &claim);
        if (pos > 0)
        {
            fixed (byte* src = _buffer)
            {
                Buffer.MemoryCopy(src, claim.Data, (long)claim.Length, MessageLength);
            }
            AeronNative.BufferClaimCommit(&claim);
        }
        return pos;
    }

    [Benchmark]
    public long Managed_TryClaim()
    {
        if (!_driverAvailable) return 0;
        var bufferClaim = new Adaptive.Aeron.LogBuffer.BufferClaim();
        long pos = _managedPub!.TryClaim(MessageLength, bufferClaim);
        if (pos > 0)
        {
            bufferClaim.Buffer.PutBytes(bufferClaim.Offset, _managedBuffer, 0, MessageLength);
            bufferClaim.Commit();
        }
        return pos;
    }

    [Benchmark]
    public unsafe int Native_Poll()
    {
        if (!_driverAvailable) return 0;
        return _nativeSub!.Poll(&NoOpHandler, 0, 10);
    }

    [Benchmark]
    public int Managed_Poll()
    {
        if (!_driverAvailable) return 0;
        return _managedSub!.Poll(NoOpFragmentHandler, 10);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void NoOpHandler(nint clientd, byte* buffer, nuint length, nint header) { }

    private static readonly Adaptive.Aeron.LogBuffer.FragmentHandler NoOpFragmentHandler =
        (buffer, offset, length, header) => { };
}
