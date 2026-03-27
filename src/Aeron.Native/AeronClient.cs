using System.Runtime.InteropServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

/// <summary>
/// High-level Aeron client wrapping aeron_t.
/// Manages the lifecycle of the native client and provides factory methods
/// for publications and subscriptions.
/// </summary>
public sealed class AeronClient : IDisposable
{
    internal nint Handle { get; private set; }
    private readonly AeronContext _context;
    private bool _disposed;

    private AeronClient(nint handle, AeronContext context)
    {
        Handle = handle;
        _context = context;
    }

    /// <summary>
    /// Connect to the media driver using the given context.
    /// Transfers ownership of the context to this client.
    /// </summary>
    public static AeronClient Connect(AeronContext context)
    {
        AeronException.ThrowIfError(AeronNative.Init(out var client, context.Handle));
        AeronException.ThrowIfError(AeronNative.Start(client));
        return new AeronClient(client, context);
    }

    /// <summary>
    /// Connect with default context.
    /// </summary>
    public static AeronClient Connect() => Connect(new AeronContext());

    public long ClientId => AeronNative.ClientId(Handle);
    public long NextCorrelationId => AeronNative.NextCorrelationId(Handle);
    public bool IsClosed => AeronNative.IsClosed(Handle);

    /// <summary>
    /// Do one unit of work on the client conductor. For use with UseConductorAgentInvoker.
    /// </summary>
    public int DoWork() => AeronNative.MainDoWork(Handle);

    /// <summary>
    /// Idle based on the amount of work done.
    /// </summary>
    public void Idle(int workCount) => AeronNative.MainIdleStrategy(Handle, workCount);

    /// <summary>
    /// Add a concurrent publication (thread-safe offer/tryClaim).
    /// Blocks until the publication is registered with the media driver.
    /// </summary>
    public Publication AddPublication(string channel, int streamId)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.AsyncAddPublication(out var async, Handle, channel, streamId));

        nint pub;
        int result;
        while ((result = AeronNative.AsyncAddPublicationPoll(out pub, async)) == 0)
        {
            Thread.Yield();
        }

        if (result < 0) throw AeronException.FromNative();
        return new Publication(pub, isExclusive: false);
    }

    /// <summary>
    /// Add an exclusive publication (single-writer, higher throughput).
    /// Blocks until the publication is registered with the media driver.
    /// </summary>
    public Publication AddExclusivePublication(string channel, int streamId)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.AsyncAddExclusivePublication(out var async, Handle, channel, streamId));

        nint pub;
        int result;
        while ((result = AeronNative.AsyncAddExclusivePublicationPoll(out pub, async)) == 0)
        {
            Thread.Yield();
        }

        if (result < 0) throw AeronException.FromNative();
        return new Publication(pub, isExclusive: true);
    }

    /// <summary>
    /// Add a subscription. Blocks until registered with the media driver.
    /// </summary>
    public unsafe Subscription AddSubscription(string channel, int streamId)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.AsyncAddSubscription(
            out var async, Handle, channel, streamId,
            null, 0, null, 0));

        nint sub;
        int result;
        while ((result = AeronNative.AsyncAddSubscriptionPoll(out sub, async)) == 0)
        {
            Thread.Yield();
        }

        if (result < 0) throw AeronException.FromNative();
        return new Subscription(sub);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Handle != 0)
        {
            AeronNative.Close(Handle);
            Handle = 0;
        }

        _context.Dispose();
    }

    // ── Static utility methods ───────────────────────────────────────────

    public static string Version
    {
        get
        {
            var ptr = AeronNative.VersionFull();
            return Marshal.PtrToStringAnsi(ptr) ?? "unknown";
        }
    }

    public static long NanoClock() => AeronNative.NanoClock();
    public static long EpochClock() => AeronNative.EpochClock();

    public static string DefaultPath
    {
        get
        {
            unsafe
            {
                var buf = stackalloc byte[512];
                AeronNative.DefaultPath(buf, 512);
                return Marshal.PtrToStringAnsi((nint)buf) ?? "";
            }
        }
    }

    public static unsafe bool IsDriverActive(string dirname, long timeoutMs = 5000)
    {
        return AeronNative.IsDriverActive(dirname, timeoutMs, null);
    }
}
