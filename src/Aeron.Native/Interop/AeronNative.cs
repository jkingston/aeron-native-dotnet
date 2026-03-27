// Raw P/Invoke bindings for the Aeron C client (libaeron).
// Uses .NET 7+ LibraryImport source generator for zero-overhead marshalling.
// Hot-path functions use [SuppressGCTransition] to eliminate the cooperative→preemptive
// GC mode switch (~5-15ns savings per call).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aeron.Native.Interop;

/// <summary>
/// Raw P/Invoke declarations for libaeron. All pointers are IntPtr (opaque handles).
/// Callers are responsible for lifetime management.
/// </summary>
internal static partial class AeronNative
{
    private const string LibName = "aeron";

    // ── Publication return value constants ───────────────────────────────────
    public const long PUBLICATION_NOT_CONNECTED = -1L;
    public const long PUBLICATION_BACK_PRESSURED = -2L;
    public const long PUBLICATION_ADMIN_ACTION = -3L;
    public const long PUBLICATION_CLOSED = -4L;
    public const long PUBLICATION_MAX_POSITION_EXCEEDED = -5L;
    public const long PUBLICATION_ERROR = -6L;

    // ── Client error constants ──────────────────────────────────────────────
    public const int CLIENT_ERROR_DRIVER_TIMEOUT = -1000;
    public const int CLIENT_ERROR_CLIENT_TIMEOUT = -1001;
    public const int CLIENT_ERROR_CONDUCTOR_SERVICE_TIMEOUT = -1002;
    public const int CLIENT_ERROR_BUFFER_FULL = -1003;

    public const long NULL_VALUE = -1L;

    // ── Controlled fragment handler actions ─────────────────────────────────
    public const int ACTION_ABORT = 1;
    public const int ACTION_BREAK = 2;
    public const int ACTION_COMMIT = 3;
    public const int ACTION_CONTINUE = 4;

    // ═══════════════════════════════════════════════════════════════════════
    // CONTEXT
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_context_init")]
    internal static partial int ContextInit(out nint context);

    [LibraryImport(LibName, EntryPoint = "aeron_context_close")]
    internal static partial int ContextClose(nint context);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_dir", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ContextSetDir(nint context, string value);

    [LibraryImport(LibName, EntryPoint = "aeron_context_get_dir")]
    internal static partial nint ContextGetDir(nint context);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_driver_timeout_ms")]
    internal static partial int ContextSetDriverTimeoutMs(nint context, ulong value);

    [LibraryImport(LibName, EntryPoint = "aeron_context_get_driver_timeout_ms")]
    internal static partial ulong ContextGetDriverTimeoutMs(nint context);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_keepalive_interval_ns")]
    internal static partial int ContextSetKeepaliveIntervalNs(nint context, ulong value);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_resource_linger_duration_ns")]
    internal static partial int ContextSetResourceLingerDurationNs(nint context, ulong value);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_idle_sleep_duration_ns")]
    internal static partial int ContextSetIdleSleepDurationNs(nint context, ulong value);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_pre_touch_mapped_memory")]
    internal static partial int ContextSetPreTouchMappedMemory(nint context, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_client_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ContextSetClientName(nint context, string value);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_use_conductor_agent_invoker")]
    internal static partial int ContextSetUseConductorAgentInvoker(nint context, [MarshalAs(UnmanagedType.I1)] bool value);

    // Error handler callback: void (*)(void* clientd, int errcode, const char* message)
    [LibraryImport(LibName, EntryPoint = "aeron_context_set_error_handler")]
    internal static unsafe partial int ContextSetErrorHandler(nint context, delegate* unmanaged[Cdecl]<nint, int, nint, void> handler, nint clientd);

    // On available image: void (*)(void* clientd, aeron_subscription_t*, aeron_image_t*)
    [LibraryImport(LibName, EntryPoint = "aeron_context_set_on_new_publication")]
    internal static unsafe partial int ContextSetOnNewPublication(nint context, delegate* unmanaged[Cdecl]<nint, nint, nint, int, int, long, void> handler, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_on_new_exclusive_publication")]
    internal static unsafe partial int ContextSetOnNewExclusivePublication(nint context, delegate* unmanaged[Cdecl]<nint, nint, nint, int, int, long, void> handler, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_on_new_subscription")]
    internal static unsafe partial int ContextSetOnNewSubscription(nint context, delegate* unmanaged[Cdecl]<nint, nint, nint, int, long, void> handler, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_on_available_counter")]
    internal static unsafe partial int ContextSetOnAvailableCounter(nint context, delegate* unmanaged[Cdecl]<nint, nint, long, int, void> handler, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_on_unavailable_counter")]
    internal static unsafe partial int ContextSetOnUnavailableCounter(nint context, delegate* unmanaged[Cdecl]<nint, nint, long, int, void> handler, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_context_set_on_close_client")]
    internal static unsafe partial int ContextSetOnCloseClient(nint context, delegate* unmanaged[Cdecl]<nint, void> handler, nint clientd);

    // ═══════════════════════════════════════════════════════════════════════
    // CLIENT LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_init")]
    internal static partial int Init(out nint client, nint context);

    [LibraryImport(LibName, EntryPoint = "aeron_start")]
    internal static partial int Start(nint client);

    [LibraryImport(LibName, EntryPoint = "aeron_main_do_work")]
    internal static partial int MainDoWork(nint client);

    [LibraryImport(LibName, EntryPoint = "aeron_main_idle_strategy")]
    internal static partial void MainIdleStrategy(nint client, int workCount);

    [LibraryImport(LibName, EntryPoint = "aeron_close")]
    internal static partial int Close(nint client);

    [LibraryImport(LibName, EntryPoint = "aeron_is_closed")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool IsClosed(nint client);

    [LibraryImport(LibName, EntryPoint = "aeron_context")]
    internal static partial nint Context(nint client);

    [LibraryImport(LibName, EntryPoint = "aeron_client_id")]
    internal static partial long ClientId(nint client);

    [LibraryImport(LibName, EntryPoint = "aeron_next_correlation_id")]
    internal static partial long NextCorrelationId(nint client);

    [LibraryImport(LibName, EntryPoint = "aeron_counters_reader")]
    internal static partial nint CountersReader(nint client);

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLICATION (async add + operations)
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_publication", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AsyncAddPublication(out nint async, nint client, string uri, int streamId);

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_publication_poll")]
    internal static partial int AsyncAddPublicationPoll(out nint publication, nint async);

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_exclusive_publication", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AsyncAddExclusivePublication(out nint async, nint client, string uri, int streamId);

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_exclusive_publication_poll")]
    internal static partial int AsyncAddExclusivePublicationPoll(out nint publication, nint async);

    // ── Hot path: Offer ─────────────────────────────────────────────────
    // aeron_publication_offer copies into the log buffer (fast memcpy for small msgs).
    // SuppressGCTransition is safe here: no callbacks, no blocking, <1us for typical payloads.

    [LibraryImport(LibName, EntryPoint = "aeron_publication_offer")]
    [SuppressGCTransition]
    internal static unsafe partial long PublicationOffer(
        nint publication, byte* buffer, nuint length,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, long> reservedValueSupplier, nint clientd);

    // Non-SuppressGCTransition variant for large messages where memcpy may exceed 1us
    [LibraryImport(LibName, EntryPoint = "aeron_publication_offer")]
    internal static unsafe partial long PublicationOfferSafe(
        nint publication, byte* buffer, nuint length,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, long> reservedValueSupplier, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_offerv")]
    [SuppressGCTransition]
    internal static unsafe partial long PublicationOfferv(
        nint publication, AeronIovec* iov, nuint iovcnt,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, long> reservedValueSupplier, nint clientd);

    // ── Hot path: TryClaim ──────────────────────────────────────────────
    // Zero-copy claim: just pointer arithmetic + CAS, always <1us.
    [LibraryImport(LibName, EntryPoint = "aeron_publication_try_claim")]
    [SuppressGCTransition]
    internal static unsafe partial long PublicationTryClaim(
        nint publication, nuint length, AeronBufferClaim* bufferClaim);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_channel_status")]
    [SuppressGCTransition]
    internal static partial long PublicationChannelStatus(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_is_closed")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool PublicationIsClosed(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_is_connected")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool PublicationIsConnected(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_constants")]
    internal static unsafe partial int PublicationConstants(nint publication, AeronPublicationConstants* constants);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_position")]
    [SuppressGCTransition]
    internal static partial long PublicationPosition(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_position_limit")]
    [SuppressGCTransition]
    internal static partial long PublicationPositionLimit(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_publication_close")]
    internal static unsafe partial int PublicationClose(
        nint publication, delegate* unmanaged[Cdecl]<nint, void> onCloseComplete, nint clientd);

    // ═══════════════════════════════════════════════════════════════════════
    // EXCLUSIVE PUBLICATION
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_offer")]
    [SuppressGCTransition]
    internal static unsafe partial long ExclusivePublicationOffer(
        nint publication, byte* buffer, nuint length,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, long> reservedValueSupplier, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_offer")]
    internal static unsafe partial long ExclusivePublicationOfferSafe(
        nint publication, byte* buffer, nuint length,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, long> reservedValueSupplier, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_offerv")]
    [SuppressGCTransition]
    internal static unsafe partial long ExclusivePublicationOfferv(
        nint publication, AeronIovec* iov, nuint iovcnt,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, long> reservedValueSupplier, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_try_claim")]
    [SuppressGCTransition]
    internal static unsafe partial long ExclusivePublicationTryClaim(
        nint publication, nuint length, AeronBufferClaim* bufferClaim);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_channel_status")]
    [SuppressGCTransition]
    internal static partial long ExclusivePublicationChannelStatus(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_is_closed")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool ExclusivePublicationIsClosed(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_is_connected")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool ExclusivePublicationIsConnected(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_constants")]
    internal static unsafe partial int ExclusivePublicationConstants(nint publication, AeronPublicationConstants* constants);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_position")]
    [SuppressGCTransition]
    internal static partial long ExclusivePublicationPosition(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_position_limit")]
    [SuppressGCTransition]
    internal static partial long ExclusivePublicationPositionLimit(nint publication);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_close")]
    internal static unsafe partial int ExclusivePublicationClose(
        nint publication, delegate* unmanaged[Cdecl]<nint, void> onCloseComplete, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_append_padding")]
    [SuppressGCTransition]
    internal static partial long ExclusivePublicationAppendPadding(nint publication, nuint length);

    [LibraryImport(LibName, EntryPoint = "aeron_exclusive_publication_offer_block")]
    [SuppressGCTransition]
    internal static unsafe partial long ExclusivePublicationOfferBlock(nint publication, byte* buffer, nuint length);

    // ═══════════════════════════════════════════════════════════════════════
    // SUBSCRIPTION
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_subscription", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int AsyncAddSubscription(
        out nint async, nint client, string uri, int streamId,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> onAvailableImage, nint onAvailableImageClientd,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> onUnavailableImage, nint onUnavailableImageClientd);

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_subscription_poll")]
    internal static partial int AsyncAddSubscriptionPoll(out nint subscription, nint async);

    // ── Hot path: Poll ──────────────────────────────────────────────────
    // NOTE: Poll invokes a callback for each fragment, so we canNOT use SuppressGCTransition
    // if the callback transitions back to managed code. However, with UnmanagedCallersOnly
    // callbacks that stay in "cooperative mode", the callback itself is fine.
    // The issue is that poll iterates and may take >1us for multiple fragments.
    // We provide both variants:

    // Fast variant for single-fragment polls with UnmanagedCallersOnly handler
    [LibraryImport(LibName, EntryPoint = "aeron_subscription_poll")]
    internal static unsafe partial int SubscriptionPoll(
        nint subscription,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> handler,
        nint clientd, nuint fragmentLimit);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_controlled_poll")]
    internal static unsafe partial int SubscriptionControlledPoll(
        nint subscription,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, int> handler,
        nint clientd, nuint fragmentLimit);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_is_connected")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SubscriptionIsConnected(nint subscription);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_constants")]
    internal static unsafe partial int SubscriptionConstants(nint subscription, AeronSubscriptionConstants* constants);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_image_count")]
    [SuppressGCTransition]
    internal static partial int SubscriptionImageCount(nint subscription);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_image_by_session_id")]
    internal static partial nint SubscriptionImageBySessionId(nint subscription, int sessionId);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_image_at_index")]
    internal static partial nint SubscriptionImageAtIndex(nint subscription, nuint index);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_image_retain")]
    internal static partial int SubscriptionImageRetain(nint subscription, nint image);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_image_release")]
    internal static partial int SubscriptionImageRelease(nint subscription, nint image);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_is_closed")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SubscriptionIsClosed(nint subscription);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_channel_status")]
    [SuppressGCTransition]
    internal static partial long SubscriptionChannelStatus(nint subscription);

    [LibraryImport(LibName, EntryPoint = "aeron_subscription_close")]
    internal static unsafe partial int SubscriptionClose(
        nint subscription, delegate* unmanaged[Cdecl]<nint, void> onCloseComplete, nint clientd);

    // ═══════════════════════════════════════════════════════════════════════
    // IMAGE
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_image_constants")]
    internal static unsafe partial int ImageConstants(nint image, AeronImageConstants* constants);

    [LibraryImport(LibName, EntryPoint = "aeron_image_position")]
    [SuppressGCTransition]
    internal static partial long ImagePosition(nint image);

    [LibraryImport(LibName, EntryPoint = "aeron_image_set_position")]
    internal static partial int ImageSetPosition(nint image, long position);

    [LibraryImport(LibName, EntryPoint = "aeron_image_is_end_of_stream")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool ImageIsEndOfStream(nint image);

    [LibraryImport(LibName, EntryPoint = "aeron_image_poll")]
    internal static unsafe partial int ImagePoll(
        nint image,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> handler,
        nint clientd, nuint fragmentLimit);

    [LibraryImport(LibName, EntryPoint = "aeron_image_controlled_poll")]
    internal static unsafe partial int ImageControlledPoll(
        nint image,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, int> handler,
        nint clientd, nuint fragmentLimit);

    [LibraryImport(LibName, EntryPoint = "aeron_image_is_closed")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool ImageIsClosed(nint image);

    // ═══════════════════════════════════════════════════════════════════════
    // BUFFER CLAIM
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_buffer_claim_commit")]
    [SuppressGCTransition]
    internal static unsafe partial int BufferClaimCommit(AeronBufferClaim* bufferClaim);

    [LibraryImport(LibName, EntryPoint = "aeron_buffer_claim_abort")]
    [SuppressGCTransition]
    internal static unsafe partial int BufferClaimAbort(AeronBufferClaim* bufferClaim);

    // ═══════════════════════════════════════════════════════════════════════
    // FRAGMENT ASSEMBLER
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_fragment_assembler_create")]
    internal static unsafe partial int FragmentAssemblerCreate(
        out nint assembler,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> delegateHandler,
        nint delegateClientd);

    [LibraryImport(LibName, EntryPoint = "aeron_fragment_assembler_delete")]
    internal static partial int FragmentAssemblerDelete(nint assembler);

    // The assembler handler itself (passed to poll as the handler, with assembler as clientd)
    [LibraryImport(LibName, EntryPoint = "aeron_fragment_assembler_handler")]
    internal static unsafe partial void FragmentAssemblerHandler(
        nint clientd, byte* buffer, nuint length, nint header);

    [LibraryImport(LibName, EntryPoint = "aeron_image_fragment_assembler_create")]
    internal static unsafe partial int ImageFragmentAssemblerCreate(
        out nint assembler,
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> delegateHandler,
        nint delegateClientd);

    [LibraryImport(LibName, EntryPoint = "aeron_image_fragment_assembler_delete")]
    internal static partial int ImageFragmentAssemblerDelete(nint assembler);

    [LibraryImport(LibName, EntryPoint = "aeron_image_fragment_assembler_handler")]
    internal static unsafe partial void ImageFragmentAssemblerHandler(
        nint clientd, byte* buffer, nuint length, nint header);

    // ═══════════════════════════════════════════════════════════════════════
    // HEADER
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_header_values")]
    internal static unsafe partial int HeaderValues(nint header, AeronHeaderValues* values);

    [LibraryImport(LibName, EntryPoint = "aeron_header_position")]
    [SuppressGCTransition]
    internal static partial long HeaderPosition(nint header);

    [LibraryImport(LibName, EntryPoint = "aeron_header_position_bits_to_shift")]
    [SuppressGCTransition]
    internal static partial nuint HeaderPositionBitsToShift(nint header);

    [LibraryImport(LibName, EntryPoint = "aeron_header_next_term_offset")]
    [SuppressGCTransition]
    internal static partial int HeaderNextTermOffset(nint header);

    // ═══════════════════════════════════════════════════════════════════════
    // COUNTER
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_counter")]
    internal static unsafe partial int AsyncAddCounter(
        out nint async, nint client, int typeId,
        byte* keyBuffer, nuint keyBufferLength,
        byte* labelBuffer, nuint labelBufferLength);

    [LibraryImport(LibName, EntryPoint = "aeron_async_add_counter_poll")]
    internal static partial int AsyncAddCounterPoll(out nint counter, nint async);

    [LibraryImport(LibName, EntryPoint = "aeron_counter_addr")]
    internal static unsafe partial long* CounterAddr(nint counter);

    [LibraryImport(LibName, EntryPoint = "aeron_counter_constants")]
    internal static unsafe partial int CounterConstants(nint counter, AeronCounterConstants* constants);

    [LibraryImport(LibName, EntryPoint = "aeron_counter_close")]
    internal static unsafe partial int CounterClose(
        nint counter, delegate* unmanaged[Cdecl]<nint, void> onCloseComplete, nint clientd);

    [LibraryImport(LibName, EntryPoint = "aeron_counter_is_closed")]
    [SuppressGCTransition]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool CounterIsClosed(nint counter);

    // ═══════════════════════════════════════════════════════════════════════
    // COUNTERS READER
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_counters_reader_max_counter_id")]
    internal static partial int CountersReaderMaxCounterId(nint reader);

    [LibraryImport(LibName, EntryPoint = "aeron_counters_reader_addr")]
    internal static unsafe partial long* CountersReaderAddr(nint reader, int counterId);

    [LibraryImport(LibName, EntryPoint = "aeron_counters_reader_counter_state")]
    internal static unsafe partial int CountersReaderCounterState(nint reader, int counterId, int* state);

    // ═══════════════════════════════════════════════════════════════════════
    // ERROR HANDLING
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_errcode")]
    internal static partial int ErrCode();

    [LibraryImport(LibName, EntryPoint = "aeron_errmsg")]
    internal static partial nint ErrMsg();

    // ═══════════════════════════════════════════════════════════════════════
    // VERSION
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_version_full")]
    internal static partial nint VersionFull();

    [LibraryImport(LibName, EntryPoint = "aeron_version_major")]
    internal static partial int VersionMajor();

    [LibraryImport(LibName, EntryPoint = "aeron_version_minor")]
    internal static partial int VersionMinor();

    [LibraryImport(LibName, EntryPoint = "aeron_version_patch")]
    internal static partial int VersionPatch();

    // ═══════════════════════════════════════════════════════════════════════
    // UTILITY
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_nano_clock")]
    [SuppressGCTransition]
    internal static partial long NanoClock();

    [LibraryImport(LibName, EntryPoint = "aeron_epoch_clock")]
    [SuppressGCTransition]
    internal static partial long EpochClock();

    [LibraryImport(LibName, EntryPoint = "aeron_is_driver_active", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static unsafe partial bool IsDriverActive(string dirname, long timeoutMs, delegate* unmanaged[Cdecl]<nint, void> logFunc);

    [LibraryImport(LibName, EntryPoint = "aeron_default_path")]
    internal static unsafe partial int DefaultPath(byte* path, nuint pathLength);

    // ═══════════════════════════════════════════════════════════════════════
    // CNC (Command and Control)
    // ═══════════════════════════════════════════════════════════════════════

    [LibraryImport(LibName, EntryPoint = "aeron_cnc_init", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CncInit(out nint cnc, string basePath, long timeoutMs);

    [LibraryImport(LibName, EntryPoint = "aeron_cnc_close")]
    internal static partial void CncClose(nint cnc);

    [LibraryImport(LibName, EntryPoint = "aeron_cnc_counters_reader")]
    internal static partial nint CncCountersReader(nint cnc);
}
