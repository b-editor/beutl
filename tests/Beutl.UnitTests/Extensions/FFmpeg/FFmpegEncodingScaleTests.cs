using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public sealed class FFmpegEncodingScaleTests
{
    [Test]
    public async Task Encode_WhenDestinationSizeDiffers_ScalesInsteadOfCropping()
    {
        string outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}.mp4");

        var controller = new FFmpegEncodingControllerProxy(outputPath, new FFmpegEncodingSettings());
        controller.VideoSettings.SourceSize = new PixelSize(64, 48);
        controller.VideoSettings.DestinationSize = new PixelSize(32, 24);
        controller.VideoSettings.FrameRate = new Rational(1, 1);
        controller.VideoSettings.Codec = new CodecRecord("libx264", "H.264 / AVC");
        controller.VideoSettings.Options.Clear();
        controller.VideoSettings.Options.Add(new AdditionalOption("preset", "fast"));
        controller.VideoSettings.Options.Add(new AdditionalOption("crf", "18"));

        try
        {
            await controller.Encode(new QuadrantFrameProvider(), new SilentSampleProvider(), CancellationToken.None);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("before establishing connection", StringComparison.Ordinal))
        {
            Assert.Ignore(ex.Message);
        }
        catch (TimeoutException ex)
        {
            Assert.Ignore(ex.Message);
        }

        using MediaReader? reader = new FFmpegDecoderInfo(new FFmpegDecodingSettings())
            .Open(outputPath, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(32, 24)));
        Assert.That(reader.ReadVideo(0, out Ref<Bitmap>? frame), Is.True);

        using (frame)
        using (Bitmap srgb = frame!.Value.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb))
        {
            Assert.Multiple(() =>
            {
                AssertDominant(srgb.GetRow<Bgra8888>(6)[6], DominantChannel.Red);
                AssertDominant(srgb.GetRow<Bgra8888>(6)[26], DominantChannel.Green);
                AssertDominant(srgb.GetRow<Bgra8888>(18)[6], DominantChannel.Blue);
                AssertBright(srgb.GetRow<Bgra8888>(18)[26]);
            });
        }
    }

    private static void AssertDominant(Bgra8888 pixel, DominantChannel channel)
    {
        const byte min = 120;
        const int margin = 35;
        switch (channel)
        {
            case DominantChannel.Red:
                Assert.That(pixel.R, Is.GreaterThan(min));
                Assert.That(pixel.R, Is.GreaterThan(pixel.G + margin));
                Assert.That(pixel.R, Is.GreaterThan(pixel.B + margin));
                break;
            case DominantChannel.Green:
                Assert.That(pixel.G, Is.GreaterThan(min));
                Assert.That(pixel.G, Is.GreaterThan(pixel.R + margin));
                Assert.That(pixel.G, Is.GreaterThan(pixel.B + margin));
                break;
            case DominantChannel.Blue:
                Assert.That(pixel.B, Is.GreaterThan(min));
                Assert.That(pixel.B, Is.GreaterThan(pixel.R + margin));
                Assert.That(pixel.B, Is.GreaterThan(pixel.G + margin));
                break;
        }
    }

    private static void AssertBright(Bgra8888 pixel)
    {
        Assert.That(pixel.R, Is.GreaterThan(160));
        Assert.That(pixel.G, Is.GreaterThan(160));
        Assert.That(pixel.B, Is.GreaterThan(160));
    }

    private enum DominantChannel
    {
        Red,
        Green,
        Blue,
    }

    private sealed class QuadrantFrameProvider : IFrameProvider
    {
        public long FrameCount => 1;

        public Rational FrameRate => new(1, 1);

        public ValueTask<Bitmap> RenderFrame(long frame)
        {
            var bitmap = new Bitmap(64, 48);
            for (int y = 0; y < bitmap.Height; y++)
            {
                Span<Bgra8888> row = bitmap.GetRow<Bgra8888>(y);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    row[x] = (x < 32, y < 24) switch
                    {
                        (true, true) => new Bgra8888(0, 0, 255, 255),
                        (false, true) => new Bgra8888(0, 255, 0, 255),
                        (true, false) => new Bgra8888(255, 0, 0, 255),
                        _ => new Bgra8888(255, 255, 255, 255),
                    };
                }
            }

            return ValueTask.FromResult(bitmap);
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
