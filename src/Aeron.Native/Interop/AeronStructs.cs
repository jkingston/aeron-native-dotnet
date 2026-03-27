// Blittable struct definitions matching the Aeron C client headers.
// All structs use LayoutKind.Sequential for direct pinning (zero-copy P/Invoke).

using System.Runtime.InteropServices;

namespace Aeron.Native.Interop;

/// <summary>
/// Matches aeron_buffer_claim_stct. Used for zero-copy publishing via TryClaim.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AeronBufferClaim
{
    public byte* FrameHeader;
    public byte* Data;
    public nuint Length;
}

/// <summary>
/// Matches aeron_iovec_stct. Scatter/gather IO vector for offerv.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AeronIovec
{
    public byte* Base;
    public nuint Length;
}

/// <summary>
/// Matches aeron_header_values_frame_stct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AeronHeaderValuesFrame
{
    public int FrameLength;
    public sbyte Version;
    public byte Flags;
    public short Type;
    public int TermOffset;
    public int SessionId;
    public int StreamId;
    public int TermId;
    public long ReservedValue;
}

/// <summary>
/// Matches aeron_header_values_stct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AeronHeaderValues
{
    public AeronHeaderValuesFrame Frame;
    public int InitialTermId;
    public nuint PositionBitsToShift;
}

/// <summary>
/// Matches aeron_publication_constants_stct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AeronPublicationConstants
{
    public byte* Channel;  // const char*
    public long OriginalRegistrationId;
    public long RegistrationId;
    public long MaxPossiblePosition;
    public nuint PositionBitsToShift;
    public nuint TermBufferLength;
    public nuint MaxMessageLength;
    public nuint MaxPayloadLength;
    public int StreamId;
    public int SessionId;
    public int InitialTermId;
    public int PublicationLimitCounterId;
    public int ChannelStatusIndicatorId;
}

/// <summary>
/// Matches aeron_subscription_constants_stct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AeronSubscriptionConstants
{
    public byte* Channel;  // const char*
    public nint OnAvailableImage;   // function pointer
    public nint OnUnavailableImage; // function pointer
    public long RegistrationId;
    public int StreamId;
    public int ChannelStatusIndicatorId;
}

/// <summary>
/// Matches aeron_image_constants_stct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AeronImageConstants
{
    public nint Subscription;       // aeron_subscription_t*
    public byte* SourceIdentity;    // const char*
    public long CorrelationId;
    public long JoinPosition;
    public nuint PositionBitsToShift;
    public nuint TermBufferLength;
    public nuint MtuLength;
    public int SessionId;
    public int InitialTermId;
    public int SubscriberPositionId;
}

/// <summary>
/// Matches aeron_counter_constants_stct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AeronCounterConstants
{
    public long RegistrationId;
    public int CounterId;
}

/// <summary>
/// Matches aeron_cnc_constants_stct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AeronCncConstants
{
    public int CncVersion;
    public int ToDriverBufferLength;
    public int ToClientsBufferLength;
    public int CounterMetadataBufferLength;
    public int CounterValuesBufferLength;
    public int ErrorLogBufferLength;
    public long ClientLivenessTimeout;
    public long StartTimestamp;
    public long Pid;
    public int FilePageSize;
}
