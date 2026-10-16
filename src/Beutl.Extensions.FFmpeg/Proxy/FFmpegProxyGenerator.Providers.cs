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
        private long _nextFrame;

        public long FrameCount => frameCount;

        public Rational FrameRate => reader.VideoInfo.FrameRate;

        public ValueTask<Bitmap?> ReadNextFrame()
        {
            long frame = _nextFrame;
            if (frame >= frameCount)
                return ValueTask.FromResult<Bitmap?>(null);

            if (!reader.ReadVideo((int)frame, out Ref<Bitmap>? bitmapRef))
                throw new InvalidOperationException($"Could not decode source frame {frame}.");

            using (bitmapRef)
            {
                _nextFrame++;
                progress?.Report(new ProxyJobProgress(
                    Math.Clamp((frame + 1) / (double)FrameCount, 0, 1),
                    null));

                return ValueTask.FromResult<Bitmap?>(bitmapRef.Value.Clone());
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

        public ValueTask<Pcm<Stereo32BitFloat>?> ReadNextSamples(int length)
        {
            return ValueTask.FromResult<Pcm<Stereo32BitFloat>?>(null);
        }

        public void Dispose()
        {
        }
    }
}
