using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

/// <summary>
/// Wraps aeron_image_t. Represents a single publication stream from a specific publisher.
/// </summary>
public sealed class Image
{
    internal nint Handle { get; }
    private readonly nint _subscriptionHandle;
    private AeronImageConstants _constants;
    private bool _constantsLoaded;

    internal Image(nint handle, nint subscriptionHandle)
    {
        Handle = handle;
        _subscriptionHandle = subscriptionHandle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int Poll(
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> handler,
        nint clientd, int fragmentLimit)
    {
        return AeronNative.ImagePoll(Handle, handler, clientd, (nuint)fragmentLimit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int ControlledPoll(
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, int> handler,
        nint clientd, int fragmentLimit)
    {
        return AeronNative.ImageControlledPoll(Handle, handler, clientd, (nuint)fragmentLimit);
    }

    public long Position => AeronNative.ImagePosition(Handle);
    public bool IsEndOfStream => AeronNative.ImageIsEndOfStream(Handle);
    public bool IsClosed => AeronNative.ImageIsClosed(Handle);

    public unsafe ref readonly AeronImageConstants Constants
    {
        get
        {
            if (!_constantsLoaded)
            {
                fixed (AeronImageConstants* p = &_constants)
                {
                    AeronException.ThrowIfError(AeronNative.ImageConstants(Handle, p));
                }
                _constantsLoaded = true;
            }
            return ref _constants;
        }
    }

    public int SessionId => Constants.SessionId;
    public long CorrelationId => Constants.CorrelationId;

    public string SourceIdentity
    {
        get
        {
            unsafe
            {
                return Marshal.PtrToStringAnsi((nint)Constants.SourceIdentity) ?? "";
            }
        }
    }

    /// <summary>
    /// Retain this image so it isn't cleaned up when the subscription detects unavailability.
    /// </summary>
    public void Retain()
    {
        AeronException.ThrowIfError(AeronNative.SubscriptionImageRetain(_subscriptionHandle, Handle));
    }

    /// <summary>
    /// Release a previously retained image.
    /// </summary>
    public void Release()
    {
        AeronException.ThrowIfError(AeronNative.SubscriptionImageRelease(_subscriptionHandle, Handle));
    }
}
