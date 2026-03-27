using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

/// <summary>
/// Wraps aeron_subscription_t. Provides high-performance polling with
/// UnmanagedCallersOnly function pointers (no delegate marshalling overhead).
/// </summary>
public sealed class Subscription : IDisposable
{
    internal nint Handle { get; private set; }
    private bool _disposed;
    private AeronSubscriptionConstants _constants;
    private bool _constantsLoaded;

    internal Subscription(nint handle)
    {
        Handle = handle;
    }

    // ── Hot path: Poll ──────────────────────────────────────────────────

    /// <summary>
    /// Poll for new messages using an unmanaged function pointer callback.
    /// This is the lowest-overhead path: no delegate allocation, no marshalling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int Poll(
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> handler,
        nint clientd, int fragmentLimit)
    {
        return AeronNative.SubscriptionPoll(Handle, handler, clientd, (nuint)fragmentLimit);
    }

    /// <summary>
    /// Poll using a managed FragmentHandler delegate. Slightly higher overhead
    /// due to GCHandle round-trip, but more ergonomic.
    /// </summary>
    public unsafe int Poll(FragmentHandler handler, int fragmentLimit)
    {
        // Pin the delegate for the duration of the poll call.
        // The GCHandle→IntPtr round-trip is the cost we pay for managed callbacks.
        var gcHandle = GCHandle.Alloc(handler);
        try
        {
            var clientd = GCHandle.ToIntPtr(gcHandle);
            return AeronNative.SubscriptionPoll(Handle, &ManagedFragmentCallback, clientd, (nuint)fragmentLimit);
        }
        finally
        {
            gcHandle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void ManagedFragmentCallback(nint clientd, byte* buffer, nuint length, nint header)
    {
        var handle = GCHandle.FromIntPtr(clientd);
        if (handle.Target is FragmentHandler handler)
        {
            handler(buffer, (int)length, header);
        }
    }

    /// <summary>
    /// Controlled poll with flow control actions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int ControlledPoll(
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, int> handler,
        nint clientd, int fragmentLimit)
    {
        return AeronNative.SubscriptionControlledPoll(Handle, handler, clientd, (nuint)fragmentLimit);
    }

    // ── Properties ──────────────────────────────────────────────────────

    public bool IsConnected => AeronNative.SubscriptionIsConnected(Handle);
    public bool IsClosed => AeronNative.SubscriptionIsClosed(Handle);
    public long ChannelStatus => AeronNative.SubscriptionChannelStatus(Handle);
    public int ImageCount => AeronNative.SubscriptionImageCount(Handle);

    public unsafe ref readonly AeronSubscriptionConstants Constants
    {
        get
        {
            if (!_constantsLoaded)
            {
                fixed (AeronSubscriptionConstants* p = &_constants)
                {
                    AeronException.ThrowIfError(AeronNative.SubscriptionConstants(Handle, p));
                }
                _constantsLoaded = true;
            }
            return ref _constants;
        }
    }

    public int StreamId => Constants.StreamId;

    public string Channel
    {
        get
        {
            unsafe
            {
                return Marshal.PtrToStringAnsi((nint)Constants.Channel) ?? "";
            }
        }
    }

    // ── Image access ────────────────────────────────────────────────────

    public Image? ImageBySessionId(int sessionId)
    {
        var ptr = AeronNative.SubscriptionImageBySessionId(Handle, sessionId);
        return ptr == 0 ? null : new Image(ptr, Handle);
    }

    public Image? ImageAtIndex(int index)
    {
        var ptr = AeronNative.SubscriptionImageAtIndex(Handle, (nuint)index);
        return ptr == 0 ? null : new Image(ptr, Handle);
    }

    // ── Cleanup ─────────────────────────────────────────────────────────

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Handle != 0)
        {
            AeronNative.SubscriptionClose(Handle, null, 0);
            Handle = 0;
        }
    }
}

/// <summary>
/// Managed fragment handler delegate.
/// </summary>
public unsafe delegate void FragmentHandler(byte* buffer, int length, nint header);
