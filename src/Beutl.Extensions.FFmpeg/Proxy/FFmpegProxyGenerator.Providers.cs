using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Extensions.FFmpeg.Proxy;

public sealed partial class FFmpegProxyGenerator
{
    private sealed class ReaderFrameProvider(
        MediaReader reader,
        long frameCount,
        IProgress<ProxyJobProgress>? progress) : IFrameProvider
    {
        public long FrameCount => frameCount;

        public Rational FrameRate => reader.VideoInfo.FrameRate;

        public ValueTask<Bitmap> RenderFrame(long frame)
        {
            if (!reader.ReadVideo((int)frame, out Ref<Bitmap>? bitmapRef))
                throw new InvalidOperationException($"Could not decode source frame {frame}.");

            using (bitmapRef)
            {
                progress?.Report(new ProxyJobProgress(
                    FrameCount <= 0 ? 0 : Math.Clamp((frame + 1) / (double)FrameCount, 0, 1),
                    null));

                return ValueTask.FromResult(bitmapRef.Value.Clone());
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class SilentSampleProvider : ISampleProvider
    {
        public long SampleCount => 0;

        public long SampleRate => 44100;

        public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
        {
            return ValueTask.FromResult(new Pcm<Stereo32BitFloat>((int)SampleRate, (int)Math.Max(0, length)));
        }

        public void Dispose()
        {
        }
    }
}
