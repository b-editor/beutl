using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg;
#else
namespace Beutl.Extensions.FFmpeg;
#endif

public static class FFmpegError
{
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
