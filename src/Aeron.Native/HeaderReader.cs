using System.Runtime.CompilerServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

/// <summary>
/// Utility for reading header values from an aeron_header_t* in fragment callbacks.
/// All operations use SuppressGCTransition as they are simple pointer reads.
/// </summary>
public static class HeaderReader
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Position(nint header) => AeronNative.HeaderPosition(header);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NextTermOffset(nint header) => AeronNative.HeaderNextTermOffset(header);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PositionBitsToShift(nint header) => (int)AeronNative.HeaderPositionBitsToShift(header);

    /// <summary>
    /// Read all header values into a managed struct. Slightly more expensive
    /// but gives access to session ID, stream ID, flags, etc.
    /// </summary>
    public static unsafe AeronHeaderValues Values(nint header)
    {
        AeronHeaderValues values;
        AeronException.ThrowIfError(AeronNative.HeaderValues(header, &values));
        return values;
    }
}
