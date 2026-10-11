using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegWorker.Decoding;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

// An output of another aspect ratio than the scene, such as a 9:16 platform preset over a 16:9 scene, gets the whole
// frame between black bars instead of a stretched one.
[TestFixture]
public sealed class LetterboxTests
{
    private string _workDir = string.Empty;

    [TestCase(1920, 1080, 1080, 1920, "1080:608", "1080:1920:0:656:black", TestName = "A9By16OutputLetterboxesA16By9Scene")]
    [TestCase(1920, 1080, 1080, 1080, "1080:608", "1080:1080:0:236:black", TestName = "ASquareOutputLetterboxesA16By9Scene")]
    [TestCase(1920, 1080, 720, 480, "720:404", "720:480:0:38:black", TestName = "A3By2OutputLetterboxesA16By9Scene")]
    [TestCase(1080, 1920, 1920, 1080, "608:1080", "1920:1080:656:0:black", TestName = "A16By9OutputPillarboxesA9By16Scene")]
    [TestCase(1920, 1080, 3840, 2160, "3840:2160", null, TestName = "TheSameAspectRatioScalesStraightUp")]
    [TestCase(1920, 1080, 1280, 720, "1280:720", null, TestName = "TheSameAspectRatioScalesStraightDown")]
    [TestCase(1920, 1080, 854, 480, "854:480", null, TestName = "AnAspectRatioWithinAPixelScalesStraight")]
    [TestCase(1920, 1080, 1366, 768, "1366:768", null, TestName = "AnotherAspectRatioWithinAPixelScalesStraight")]
    // A half-size proxy rounds both extents to even numbers: 500x281.5 becomes 500x280.
    [TestCase(1000, 563, 500, 280, "500:280", null, TestName = "AProxyRoundedToEvenExtentsScalesStraight")]
    public void TheFiltersFitTheWholeFrame(
        int sourceWidth, int sourceHeight, int width, int height, string scale, string? pad)
    {
        (string actualScale, string? actualPad) = FrameFit.GetFilterArguments(
            new PixelSize(sourceWidth, sourceHeight), new PixelSize(width, height));

        Assert.Multiple(() =>
        {
            Assert.That(actualScale, Is.EqualTo(scale));
            Assert.That(actualPad, Is.EqualTo(pad));
        });
    }

    // 64x36 into 36x64 scales the picture to 36x20 and pads 22 rows above it and 22 below.
    [Test]
    public async Task AnEncoded9By16OutputOfA16By9Frame_HasTheWholeFrameBetweenBlackBars([Values] bool hdr)
    {
        using Bitmap decoded = await EncodeAndDecodeFirstFrame(hdr, new PixelSize(64, 36), new PixelSize(36, 64));

        AssertRows(decoded, hdr, pictureRows: (22, 42));
    }

    [Test]
    public async Task AnEncodedOutputOfTheSameAspectRatio_HasNoBars([Values] bool hdr)
    {
        using Bitmap decoded = await EncodeAndDecodeFirstFrame(hdr, new PixelSize(64, 36), new PixelSize(32, 18));

        AssertRows(decoded, hdr, pictureRows: (0, 18));
    }

    [SetUp]
    public void SetUp()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-ffmpeg-letterbox-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_workDir, recursive: true);
    }

    // FFV1 keeps every pixel, so the bars and the picture are checked on what the encoder produced.
    private async Task<Bitmap> EncodeAndDecodeFirstFrame(bool hdr, PixelSize source, PixelSize destination)
    {
        try
        {
            FFmpegLoaderWorker.Initialize();
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            Assert.Ignore("FFmpeg native libraries are not available.");
        }
        catch (DllNotFoundException)
        {
            Assert.Ignore("FFmpeg native library dependencies are not available.");
        }

        AVPixelFormat encodedFormat = hdr ? AVPixelFormat.AV_PIX_FMT_GBRAP16LE : AVPixelFormat.AV_PIX_FMT_BGRA;
        MediaCodec? codec = MediaCodec.FindEncoder("ffv1");
        if (codec == null || !codec.GetPixelFmts().Contains(encodedFormat))
            Assert.Ignore($"The FFV1 encoder does not support {encodedFormat}.");

        string outputPath = Path.Combine(_workDir, "fitted.mkv");
        var controller = new FFmpegEncodingController(outputPath, new FFmpegEncodingSettings());
        controller.VideoSettings.SourceSize = source;
        controller.VideoSettings.DestinationSize = destination;
        controller.VideoSettings.FrameRate = new Rational(30, 1);
        controller.VideoSettings.Codec = new CodecRecord("ffv1", "FFV1");
        controller.VideoSettings.Options.Clear();
        controller.VideoSettings.Format = (int)encodedFormat;
        controller.VideoSettings.ColorTrc = hdr ? FFColorTransfer.SMPTE2084 : FFColorTransfer.IEC61966_2_1;
        controller.VideoSettings.ColorPrimaries = hdr ? FFColorPrimaries.BT2020 : FFColorPrimaries.BT709;
        controller.VideoSettings.ColorSpace = FFColorSpace.RGB;
        controller.VideoSettings.ColorRange = FFColorRange.JPEG;
        controller.AudioSettings.Codec = new CodecRecord("pcm_s16le", "PCM");
        controller.AudioSettings.Format = FFmpegAudioEncoderSettings.AudioFormat.S16;
        using var frames = new SolidFrameProvider(source);
        using var samples = new EmptySampleProvider();

        await controller.Encode(frames, samples, CancellationToken.None);

        using var reader = new FFmpegReader(outputPath, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());
        Assert.That(reader.ReadVideo(0, out Ref<Bitmap>? decoded), Is.True);
        using (decoded)
        {
            return decoded!.Value.Clone();
        }
    }

    // Rows inside pictureRows (start inclusive, end exclusive) carry the picture; every other row is black.
    private static void AssertRows(Bitmap decoded, bool hdr, (int Start, int End) pictureRows)
    {
        byte[] black = hdr ? [0, 0, 0, 0, 0, 0, 0xFF, 0xFF] : [0, 0, 0, 0xFF];
        byte[] picture = ExpectedPicturePixel(hdr);
        Assert.That(decoded.BytesPerPixel, Is.EqualTo(black.Length));

        for (int y = 0; y < decoded.Height; y++)
        {
            ReadOnlySpan<byte> row = decoded.GetRow(y);
            bool inPicture = y >= pictureRows.Start && y < pictureRows.End;
            for (int x = 0; x < decoded.Width; x++)
            {
                ReadOnlySpan<byte> pixel = row.Slice(x * black.Length, black.Length);
                if (!inPicture)
                {
                    Assert.That(pixel.SequenceEqual(black), Is.True, $"({x}, {y}) is in a bar and must be black.");
                    continue;
                }

                // Scaling a uniform picture leaves it uniform up to the scaler's fixed-point rounding.
                for (int channel = 0; channel < 4; channel++)
                {
                    Assert.That(
                        Channel(pixel, channel, hdr),
                        Is.EqualTo(Channel(picture, channel, hdr)).Within(hdr ? 64 : 1),
                        $"({x}, {y}) is in the picture; channel {channel}.");
                }
            }
        }

        static int Channel(ReadOnlySpan<byte> pixel, int channel, bool wide)
            => wide ? pixel[channel * 2] | (pixel[channel * 2 + 1] << 8) : pixel[channel];
    }

    // The color the encoder hands FFmpeg for the source frames, in the layout the reader returns.
    private static byte[] ExpectedPicturePixel(bool hdr)
    {
        using Bitmap source = SolidFrameProvider.CreateFrame(new PixelSize(1, 1));
        BitmapColorSpace colorSpace = hdr
            ? ColorSpaceHelper.BuildHdrColorSpace(
                AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084, AVColorPrimaries.AVCOL_PRI_BT2020)
            : BitmapColorSpace.Srgb;
        using Bitmap expected = source.Convert(
            hdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888,
            hdr ? BitmapAlphaType.Unpremul : BitmapAlphaType.Premul,
            colorSpace);
        return expected.GetRow(0).ToArray();
    }

    private sealed class SolidFrameProvider(PixelSize size) : IFrameProvider
    {
        public long FrameCount => 1;

        public Rational FrameRate => new(30, 1);

        public static Bitmap CreateFrame(PixelSize size)
        {
            var bitmap = new Bitmap(size.Width, size.Height);
            for (int y = 0; y < size.Height; y++)
            {
                Span<byte> row = bitmap.GetRow(y);
                for (int x = 0; x < size.Width; x++)
                {
                    row[x * 4] = 40;
                    row[x * 4 + 1] = 160;
                    row[x * 4 + 2] = 200;
                    row[x * 4 + 3] = 255;
                }
            }

            return bitmap;
        }

        public ValueTask<Bitmap> RenderFrame(long frame) => ValueTask.FromResult(CreateFrame(size));

        public void Dispose()
        {
        }
    }

    private sealed class EmptySampleProvider : ISampleProvider
    {
        public long SampleCount => 0;

        public long SampleRate => 44100;

        public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length) =>
            throw new AssertionException("There are no audio samples to encode.");

        public void Dispose()
        {
        }
    }
}
