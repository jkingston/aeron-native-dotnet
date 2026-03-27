using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

/// <summary>
/// Configuration context for an Aeron client instance.
/// Wraps aeron_context_t with a fluent API.
/// </summary>
public sealed class AeronContext : IDisposable
{
    internal nint Handle { get; private set; }
    private bool _disposed;

    // Must keep delegates alive for the lifetime of the context to prevent GC collection.
    // For UnmanagedCallersOnly static methods, this isn't needed, but we keep references
    // to any GCHandles used for clientd pointers.
    private GCHandle _errorHandlerHandle;

    public AeronContext()
    {
        AeronException.ThrowIfError(AeronNative.ContextInit(out var ctx));
        Handle = ctx;
    }

    public AeronContext AeronDir(string dir)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetDir(Handle, dir));
        return this;
    }

    public AeronContext DriverTimeoutMs(ulong timeoutMs)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetDriverTimeoutMs(Handle, timeoutMs));
        return this;
    }

    public AeronContext KeepaliveIntervalNs(ulong intervalNs)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetKeepaliveIntervalNs(Handle, intervalNs));
        return this;
    }

    public AeronContext ResourceLingerDurationNs(ulong durationNs)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetResourceLingerDurationNs(Handle, durationNs));
        return this;
    }

    public AeronContext IdleSleepDurationNs(ulong durationNs)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetIdleSleepDurationNs(Handle, durationNs));
        return this;
    }

    public AeronContext PreTouchMappedMemory(bool preTouchMappedMemory)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetPreTouchMappedMemory(Handle, preTouchMappedMemory));
        return this;
    }

    public AeronContext ClientName(string name)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetClientName(Handle, name));
        return this;
    }

    public AeronContext UseConductorAgentInvoker(bool value)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetUseConductorAgentInvoker(Handle, value));
        return this;
    }

    /// <summary>
    /// Set a static error handler via UnmanagedCallersOnly function pointer.
    /// </summary>
    public unsafe AeronContext ErrorHandler(delegate* unmanaged[Cdecl]<nint, int, nint, void> handler, nint clientd = 0)
    {
        ThrowIfDisposed();
        AeronException.ThrowIfError(AeronNative.ContextSetErrorHandler(Handle, handler, clientd));
        return this;
    }

    /// <summary>
    /// Set an error handler using a managed Action. Allocates a GCHandle for the delegate.
    /// </summary>
    public unsafe AeronContext ErrorHandler(Action<int, string> handler)
    {
        ThrowIfDisposed();

        if (_errorHandlerHandle.IsAllocated) _errorHandlerHandle.Free();

        _errorHandlerHandle = GCHandle.Alloc(handler);
        var clientd = GCHandle.ToIntPtr(_errorHandlerHandle);

        AeronException.ThrowIfError(AeronNative.ContextSetErrorHandler(
            Handle, &ManagedErrorHandler, clientd));
        return this;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ManagedErrorHandler(nint clientd, int errcode, nint message)
    {
        var handle = GCHandle.FromIntPtr(clientd);
        if (handle.Target is Action<int, string> handler)
        {
            var msg = Marshal.PtrToStringAnsi(message) ?? "";
            handler(errcode, msg);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_errorHandlerHandle.IsAllocated) _errorHandlerHandle.Free();
        if (Handle != 0)
        {
            AeronNative.ContextClose(Handle);
            Handle = 0;
        }
    }
}
