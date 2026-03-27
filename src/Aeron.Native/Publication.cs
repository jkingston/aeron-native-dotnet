using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

/// <summary>
/// Wraps both aeron_publication_t and aeron_exclusive_publication_t.
/// Provides zero-allocation offer/tryClaim for the hot path.
/// </summary>
public sealed class Publication : IDisposable
{
    // Mirror the C constants for caller convenience
    public const long NotConnected = AeronNative.PUBLICATION_NOT_CONNECTED;
    public const long BackPressured = AeronNative.PUBLICATION_BACK_PRESSURED;
    public const long AdminAction = AeronNative.PUBLICATION_ADMIN_ACTION;
    public const long Closed = AeronNative.PUBLICATION_CLOSED;
    public const long MaxPositionExceeded = AeronNative.PUBLICATION_MAX_POSITION_EXCEEDED;
    public const long Error = AeronNative.PUBLICATION_ERROR;

    internal nint Handle { get; private set; }
    private readonly bool _isExclusive;
    private bool _disposed;

    // Cached constants (read once from native)
    private AeronPublicationConstants _constants;
    private bool _constantsLoaded;

    internal Publication(nint handle, bool isExclusive)
    {
        Handle = handle;
        _isExclusive = isExclusive;
    }

    // ── Hot path: Offer (with SuppressGCTransition) ─────────────────────

    /// <summary>
    /// Offer a message from a pinned byte*. Zero-allocation, minimal latency.
    /// Uses SuppressGCTransition for messages under ~1KB.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long Offer(byte* buffer, int length)
    {
        return _isExclusive
            ? AeronNative.ExclusivePublicationOffer(Handle, buffer, (nuint)length, null, 0)
            : AeronNative.PublicationOffer(Handle, buffer, (nuint)length, null, 0);
    }

    /// <summary>
    /// Offer from a Span. Pins the span on the stack (zero-alloc with fixed).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long Offer(ReadOnlySpan<byte> buffer)
    {
        fixed (byte* ptr = buffer)
        {
            return Offer(ptr, buffer.Length);
        }
    }

    /// <summary>
    /// Offer from a byte array with offset and length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long Offer(byte[] buffer, int offset, int length)
    {
        fixed (byte* ptr = &buffer[offset])
        {
            return Offer(ptr, length);
        }
    }

    /// <summary>
    /// Offer for large messages (>1KB). Does not use SuppressGCTransition.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long OfferSafe(byte* buffer, int length)
    {
        return _isExclusive
            ? AeronNative.ExclusivePublicationOfferSafe(Handle, buffer, (nuint)length, null, 0)
            : AeronNative.PublicationOfferSafe(Handle, buffer, (nuint)length, null, 0);
    }

    // ── Hot path: Scatter/Gather Offer ──────────────────────────────────

    /// <summary>
    /// Vectored offer for multi-part messages without copying into a contiguous buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long Offerv(AeronIovec* iov, int iovcnt)
    {
        return _isExclusive
            ? AeronNative.ExclusivePublicationOfferv(Handle, iov, (nuint)iovcnt, null, 0)
            : AeronNative.PublicationOfferv(Handle, iov, (nuint)iovcnt, null, 0);
    }

    // ── Hot path: TryClaim (zero-copy) ──────────────────────────────────

    /// <summary>
    /// Claim space in the publication log buffer for zero-copy writing.
    /// Returns the new stream position on success, or a negative error code.
    /// The caller must write to claim.Data and then call Commit() or Abort().
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long TryClaim(int length, AeronBufferClaim* claim)
    {
        return _isExclusive
            ? AeronNative.ExclusivePublicationTryClaim(Handle, (nuint)length, claim)
            : AeronNative.PublicationTryClaim(Handle, (nuint)length, claim);
    }

    /// <summary>
    /// Claim space and write via a callback, auto-committing on success.
    /// Zero-copy: the writer writes directly into the publication's term buffer.
    /// </summary>
    public unsafe long TryClaim(int length, TryClaimWriter writer)
    {
        AeronBufferClaim claim;
        var position = TryClaim(length, &claim);
        if (position > 0)
        {
            try
            {
                writer(new Span<byte>(claim.Data, (int)claim.Length));
                AeronNative.BufferClaimCommit(&claim);
            }
            catch
            {
                AeronNative.BufferClaimAbort(&claim);
                throw;
            }
        }
        return position;
    }

    // ── Properties (SuppressGCTransition - all are simple reads) ────────

    public bool IsConnected
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isExclusive
            ? AeronNative.ExclusivePublicationIsConnected(Handle)
            : AeronNative.PublicationIsConnected(Handle);
    }

    public bool IsClosed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isExclusive
            ? AeronNative.ExclusivePublicationIsClosed(Handle)
            : AeronNative.PublicationIsClosed(Handle);
    }

    public long Position => _isExclusive
        ? AeronNative.ExclusivePublicationPosition(Handle)
        : AeronNative.PublicationPosition(Handle);

    public long PositionLimit => _isExclusive
        ? AeronNative.ExclusivePublicationPositionLimit(Handle)
        : AeronNative.PublicationPositionLimit(Handle);

    public long ChannelStatus => _isExclusive
        ? AeronNative.ExclusivePublicationChannelStatus(Handle)
        : AeronNative.PublicationChannelStatus(Handle);

    public bool IsExclusive => _isExclusive;

    public unsafe ref readonly AeronPublicationConstants Constants
    {
        get
        {
            if (!_constantsLoaded)
            {
                fixed (AeronPublicationConstants* p = &_constants)
                {
                    if (_isExclusive)
                        AeronException.ThrowIfError(AeronNative.ExclusivePublicationConstants(Handle, p));
                    else
                        AeronException.ThrowIfError(AeronNative.PublicationConstants(Handle, p));
                }
                _constantsLoaded = true;
            }
            return ref _constants;
        }
    }

    public int StreamId => Constants.StreamId;
    public int SessionId => Constants.SessionId;
    public int MaxMessageLength => (int)Constants.MaxMessageLength;
    public int MaxPayloadLength => (int)Constants.MaxPayloadLength;
    public int TermBufferLength => (int)Constants.TermBufferLength;

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

    // ── Exclusive publication extras ────────────────────────────────────

    public unsafe long AppendPadding(int length)
    {
        if (!_isExclusive) throw new InvalidOperationException("AppendPadding requires an exclusive publication.");
        return AeronNative.ExclusivePublicationAppendPadding(Handle, (nuint)length);
    }

    public unsafe long OfferBlock(byte* buffer, int length)
    {
        if (!_isExclusive) throw new InvalidOperationException("OfferBlock requires an exclusive publication.");
        return AeronNative.ExclusivePublicationOfferBlock(Handle, buffer, (nuint)length);
    }

    // ── Cleanup ─────────────────────────────────────────────────────────

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Handle != 0)
        {
            if (_isExclusive)
                AeronNative.ExclusivePublicationClose(Handle, null, 0);
            else
                AeronNative.PublicationClose(Handle, null, 0);
            Handle = 0;
        }
    }
}

/// <summary>
/// Delegate for writing into a claimed buffer region.
/// </summary>
public delegate void TryClaimWriter(Span<byte> buffer);
