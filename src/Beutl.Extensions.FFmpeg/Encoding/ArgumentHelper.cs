#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal static class ArgumentHelper
{
    public static T When<T>(this T self, bool condition, Func<T, T> func)
    {
        if (condition)
        {
            return func(self);
        }
        else
        {
            return self;
        }
    }
}
