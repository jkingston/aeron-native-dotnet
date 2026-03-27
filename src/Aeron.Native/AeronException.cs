using System.Runtime.InteropServices;
using Aeron.Native.Interop;

namespace Aeron.Native;

public class AeronException : Exception
{
    public int ErrorCode { get; }

    public AeronException(string message, int errorCode = 0) : base(message)
    {
        ErrorCode = errorCode;
    }

    internal static AeronException FromNative()
    {
        var code = AeronNative.ErrCode();
        var msgPtr = AeronNative.ErrMsg();
        var msg = msgPtr != 0 ? Marshal.PtrToStringAnsi(msgPtr) ?? "Unknown error" : "Unknown error";
        return new AeronException(msg, code);
    }

    internal static void ThrowIfError(int result)
    {
        if (result < 0) throw FromNative();
    }
}
