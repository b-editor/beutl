using FFmpeg.AutoGen;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal static class ExceptionHandler
{
    internal delegate void ErrorHandler(int errorCode);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ThrowIfError(this int errorCode, string exceptionMessage)
    {
        if (errorCode < 0)
        {
            throw new Exception($"{exceptionMessage}\n\n{DecodeMessage(errorCode)}");
        }
    }

    internal static int IfError(this int errorCode, int handledError, ErrorHandler action, bool handles = true)
    {
        if (errorCode == handledError)
        {
            action(errorCode);
        }

        return handles ? 0 : errorCode;
    }

    internal static int IfError(this int errorCode, int handledError, string exceptionMessage)
        => errorCode.IfError(handledError, x => throw new Exception(exceptionMessage));

    private static string Utf8ToString(this IntPtr pointer)
    {
        var lenght = 0;

        while (Marshal.ReadByte(pointer, lenght) != 0)
        {
            ++lenght;
        }

        var buffer = new byte[lenght];
        Marshal.Copy(pointer, buffer, 0, lenght);

        return System.Text.Encoding.UTF8.GetString(buffer);
    }

    private static unsafe string DecodeMessage(int errorCode)
    {
        const int bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(errorCode, buffer, bufferSize);

        var message = new IntPtr(buffer).Utf8ToString();
        return message;
    }
}
