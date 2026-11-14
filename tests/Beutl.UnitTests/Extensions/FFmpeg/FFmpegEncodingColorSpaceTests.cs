using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Extensions.FFmpeg;

// A proxy's frames come straight from the decoder, in the video's color space. The worker read them as sRGB, so a
// BT.709 video's proxy came out darker in the midtones: code value 117, sRGB 128, read back as 116 (#4291).
[TestFixture]
public sealed class FFmpegEncodingColorSpaceTests
{
    [Test]
    public async Task A_frame_in_the_BT709_color_space_encodes_as_it_looks()
    {
        if (!FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
            Assert.Ignore("FFmpeg worker deployment is unavailable; skipping the native integration test.");

        string outputPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.mp4");
        var controller = new FFmpegEncodingControllerProxy(outputPath, new FFmpegEncodingSettings());
        controller.VideoSettings.SourceSize.Value = new PixelSize(64, 48);
        controller.VideoSettings.DestinationSize.Value = new PixelSize(64, 48);
        controller.VideoSettings.FrameRate.Value = new Rational(1, 1);
        controller.VideoSettings.Codec.Value = new CodecRecord("libx264", "H.264 / AVC");
        controller.VideoSettings.Options.Value.Clear();
        controller.VideoSettings.Options.Value.Add(new AdditionalOption("crf", "1"));

        BitmapColorSpace bt709 = BitmapColorSpaceMapping.BuildTargetColorSpace(
            BitmapColorTransfer.Bt709, BitmapColorPrimaries.Bt709);
        using var frames = new GrayFrameProvider(117, bt709);
        int expected;
        using (Bitmap source = frames.CreateFrame())
        using (Bitmap srgb = source.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb))
            expected = srgb.GetRow<Bgra8888>(24)[32].G;

        try
        {
            await controller.Encode(frames, new SilentSampleProvider(), CancellationToken.None);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore(ex.Message);
        }

        try
        {
            using MediaReader? reader = new FFmpegDecoderInfo(new FFmpegDecodingSettings())
                .Open(outputPath, new MediaOptions(MediaMode.Video));
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader!.ReadVideo(0, out Ref<Bitmap>? frame), Is.True);
            using (frame)
            using (Bitmap srgb = frame!.Value.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb))
            {
                Bgra8888 pixel = srgb.GetRow<Bgra8888>(24)[32];
                // Two for the YUV round trip, as #4287's conversion can read up to 2 low.
                Assert.That(new int[] { pixel.R, pixel.G, pixel.B }, Is.All.EqualTo(expected).Within(2),
                    $"sRGB {expected} read back as {pixel.G}");
            }
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private sealed class GrayFrameProvider(byte code, BitmapColorSpace colorSpace) : IFrameProvider
    {
        private bool _read;

        public long FrameCount => 1;

        public Rational FrameRate => new(1, 1);

        public Bitmap CreateFrame()
        {
            var bitmap = new Bitmap(64, 48, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, colorSpace);
            for (int y = 0; y < bitmap.Height; y++)
                bitmap.GetRow<Bgra8888>(y).Fill(new Bgra8888(code, code, code, 255));

            return bitmap;
        }

        public ValueTask<Bitmap?> ReadNextFrame()
        {
            if (_read)
                return ValueTask.FromResult<Bitmap?>(null);

            _read = true;
            return ValueTask.FromResult<Bitmap?>(CreateFrame());
        }

        public void Dispose()
        {
        }
    }

    private sealed class SilentSampleProvider : ISampleProvider
    {
        public AudioChannelLayout ChannelLayout => AudioChannelLayout.Stereo;

        public long SampleCount => 0;

        public long SampleRate => 44100;

        public ValueTask<AudioBuffer?> ReadNextSamples(int length) => ValueTask.FromResult<AudioBuffer?>(null);

        public void Dispose()
        {
        }
    }
}
