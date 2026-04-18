namespace Beutl.Extensions.AVFoundation.Interop;

internal sealed class BeutlAVFException : Exception
{
    public BeutlAVFException(int code, string message) : base($"{message} (code={code})")
    {
        Code = code;
    }

    public int Code { get; }

    public static void ThrowIfFailed(int result)
    {
        if (result == 0) return;
        throw new BeutlAVFException(result, BeutlAVFNative.GetLastErrorMessage());
    }
}
