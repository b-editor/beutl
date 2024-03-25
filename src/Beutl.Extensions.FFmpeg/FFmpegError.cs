using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg;
#else
namespace Beutl.Extensions.FFmpeg;
#endif

public static class FFmpegError
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

    public static unsafe string DecodeMessage(int errorCode)
    {
        const int bufferSize = 1024;
        Span<byte> buffer = stackalloc byte[bufferSize];
        fixed (byte* bufferPtr = buffer)
        {
            ffmpeg.av_strerror(errorCode, bufferPtr, bufferSize);
        }

        int length = 0;
        foreach (byte item in buffer)
        {
            if (item != 0)
            {
                length++;
            }
        }

        string message = System.Text.Encoding.UTF8.GetString(buffer.Slice(0, length));
        return message;
    }
}
