using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

/// <summary>
/// Wraps aeron_fragment_assembler_t. Reassembles fragmented messages before
/// delivering them to the user's callback. For unfragmented messages, passes
/// through with zero overhead in the C implementation.
/// </summary>
public sealed class FragmentAssembler : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// Create a fragment assembler that delivers reassembled messages via the given handler.
    /// The handler is an UnmanagedCallersOnly function pointer for minimal overhead.
    /// </summary>
    public unsafe FragmentAssembler(
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> handler,
        nint clientd = 0)
    {
        AeronException.ThrowIfError(
            AeronNative.FragmentAssemblerCreate(out _handle, handler, clientd));
    }

    /// <summary>
    /// The handler function to pass to Subscription.Poll or Image.Poll.
    /// This is the assembler's own handler that performs reassembly.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Get the assembler handler as a function pointer for use with Poll.
    /// Usage: subscription.Poll(assembler.HandlerPointer, assembler.Handle, fragmentLimit)
    /// </summary>
    public static unsafe delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> HandlerFunctionPointer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => &AssemblerHandler;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void AssemblerHandler(nint clientd, byte* buffer, nuint length, nint header)
    {
        AeronNative.FragmentAssemblerHandler(clientd, buffer, length, header);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != 0)
        {
            AeronNative.FragmentAssemblerDelete(_handle);
            _handle = 0;
        }
    }
}

/// <summary>
/// Wraps aeron_image_fragment_assembler_t for per-image fragment reassembly.
/// </summary>
public sealed class ImageFragmentAssembler : IDisposable
{
    private nint _handle;
    private bool _disposed;

    public unsafe ImageFragmentAssembler(
        delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> handler,
        nint clientd = 0)
    {
        AeronException.ThrowIfError(
            AeronNative.ImageFragmentAssemblerCreate(out _handle, handler, clientd));
    }

    public nint Handle => _handle;

    public static unsafe delegate* unmanaged[Cdecl]<nint, byte*, nuint, nint, void> HandlerFunctionPointer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => &AssemblerHandler;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void AssemblerHandler(nint clientd, byte* buffer, nuint length, nint header)
    {
        AeronNative.ImageFragmentAssemblerHandler(clientd, buffer, length, header);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != 0)
        {
            AeronNative.ImageFragmentAssemblerDelete(_handle);
            _handle = 0;
        }
    }
}
