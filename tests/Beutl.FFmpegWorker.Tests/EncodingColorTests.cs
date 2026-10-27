using Beutl.FFmpegIpc;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class EncodingColorTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        FFmpegTestHelpers.RequireFFmpeg();
        _directory = Path.Combine(Path.GetTempPath(), "beutl-encoding-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (_directory != null) Directory.Delete(_directory, recursive: true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task H264PixelsMatchTheDeclaredMatrixTransferAndRange(bool explicitSettings)
    {
        var controller = CreateController("bars.mp4");
        if (explicitSettings)
        {
            controller.VideoSettings.ColorPrimaries.Value = FFColorPrimaries.BT709;
            controller.VideoSettings.ColorTrc.Value = FFColorTransfer.IEC61966_2_1;
            controller.VideoSettings.ColorSpace.Value = FFColorSpace.BT470BG;
            controller.VideoSettings.ColorRange.Value = FFColorRange.JPEG;
        }

        await Encode(controller);
        using MediaFrame frame = ReadFirstFrame(controller.OutputFile);
        Assert.Multiple(() =>
        {
            Assert.That(frame.ColorPrimaries, Is.EqualTo(AVColorPrimaries.AVCOL_PRI_BT709));
            Assert.That(frame.ColorTrc, Is.EqualTo(explicitSettings
                ? AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1 : AVColorTransferCharacteristic.AVCOL_TRC_BT709));
            Assert.That(frame.Colorspace, Is.EqualTo(explicitSettings ? AVColorSpace.AVCOL_SPC_BT470BG : AVColorSpace.AVCOL_SPC_BT709));
            Assert.That(frame.ColorRange, Is.EqualTo(explicitSettings ? AVColorRange.AVCOL_RANGE_JPEG : AVColorRange.AVCOL_RANGE_MPEG));
        });

        // Independent reference values for red, green, blue, sRGB mid-gray, black and white.
        // sRGB 128 becomes BT.709 R'G'B' 115 before limited-range YUV conversion.
        int[][] expected = explicitSettings
            ? [[76, 85, 255], [150, 44, 21], [29, 255, 107], [128, 128, 128], [0, 128, 128], [255, 128, 128]]
            : [[63, 102, 240], [173, 42, 26], [32, 240, 118], [115, 128, 128], [16, 128, 128], [235, 128, 128]];
        AssertYuvPatches(frame, expected);
        Assert.That(controller.VideoSettings.ColorTrc.Value,
            Is.EqualTo(explicitSettings ? FFColorTransfer.IEC61966_2_1 : FFColorTransfer.UNSPECIFIED),
            "Resolving defaults must not overwrite saved settings.");
    }

    // AVI records no colour description, so the BT.601 matrix that readers assume for untagged video stays.
    [Test]
    public async Task UntaggableOutputKeepsTheUntaggedBt601Matrix()
    {
        var controller = CreateController("bars.avi");
        await Encode(controller);
        using MediaFrame frame = ReadFirstFrame(controller.OutputFile);
        Assert.Multiple(() =>
        {
            Assert.That(frame.ColorPrimaries, Is.EqualTo(AVColorPrimaries.AVCOL_PRI_UNSPECIFIED));
            Assert.That(frame.ColorTrc, Is.EqualTo(AVColorTransferCharacteristic.AVCOL_TRC_UNSPECIFIED));
            Assert.That(frame.Colorspace, Is.EqualTo(AVColorSpace.AVCOL_SPC_UNSPECIFIED));
        });
        AssertYuvPatches(frame, [[81, 90, 240], [145, 54, 34], [41, 240, 110], [126, 128, 128], [16, 128, 128], [235, 128, 128]]);
    }

    [Test]
    public async Task MatrixSwscaleCannotProduceStillEncodesWithItsTag()
    {
        var controller = CreateController("ictcp.mp4");
        controller.VideoSettings.ColorSpace.Value = FFColorSpace.ICTCP;
        await Encode(controller);
        using MediaFrame frame = ReadFirstFrame(controller.OutputFile);
        Assert.That(frame.Colorspace, Is.EqualTo(AVColorSpace.AVCOL_SPC_ICTCP));
    }

    [TestCase(FFColorTransfer.UNSPECIFIED)]
    [TestCase(FFColorTransfer.LOG)]
    [TestCase(FFColorTransfer.LOG_SQRT)]
    public async Task RgbOutputTagsMatchTheSrgbPixels(FFColorTransfer transfer)
    {
        var controller = CreateController("rgb.mkv");
        controller.VideoSettings.Codec.Value = new CodecRecord("ffv1", "FFV1");
        controller.VideoSettings.Format.Value = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        controller.VideoSettings.Options.Value.Clear();
        controller.VideoSettings.ColorTrc.Value = transfer;
        await Encode(controller);
        using MediaFrame frame = ReadFirstFrame(controller.OutputFile);
        Assert.Multiple(() =>
        {
            Assert.That(frame.Colorspace, Is.EqualTo(AVColorSpace.AVCOL_SPC_RGB));
            Assert.That(frame.ColorRange, Is.EqualTo(AVColorRange.AVCOL_RANGE_JPEG));
            Assert.That(frame.ColorTrc, Is.EqualTo(AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1));
        });
        using Bitmap source = ColorBarsFrameProvider.CreateFrame();
        unsafe
        {
            for (int y = 0; y < source.Height; y++)
                Assert.That(new ReadOnlySpan<byte>(frame.Data[0] + y * frame.Linesize[0], source.Width * 4)
                    .SequenceEqual(source.GetRow(y)), Is.True);
        }
        Assert.That(controller.VideoSettings.ColorTrc.Value, Is.EqualTo(transfer), "Keep the saved selection unchanged.");
    }

    [Test]
    public async Task JpegDefaultsKeepFullRangeSrgbPixels()
    {
        var controller = CreateController("jpeg.mkv");
        controller.VideoSettings.Codec.Value = new CodecRecord("mjpeg", "MJPEG");
        controller.VideoSettings.Format.Value = (int)AVPixelFormat.AV_PIX_FMT_YUVJ420P;
        controller.VideoSettings.Options.Value.Clear();
        await Encode(controller);
        using MediaFrame frame = ReadFirstFrame(controller.OutputFile);
        Assert.That(frame.ColorRange, Is.EqualTo(AVColorRange.AVCOL_RANGE_JPEG));
        AssertYuvPatches(frame, [[76, 85, 255], [150, 44, 21], [29, 255, 107], [128, 128, 128], [0, 128, 128], [255, 128, 128]]);
    }

    [TestCase(FFColorTransfer.SMPTE2084)]
    [TestCase(FFColorTransfer.ARIB_STD_B67)]
    public async Task HdrDefaultsKeepTheHdrTransferAndUseRec2020(FFColorTransfer transfer)
    {
        var controller = CreateController("hdr.mkv");
        controller.VideoSettings.Codec.Value = new CodecRecord("ffv1", "FFV1");
        controller.VideoSettings.Format.Value = (int)AVPixelFormat.AV_PIX_FMT_YUV420P10LE;
        controller.VideoSettings.Options.Value.Clear();
        controller.VideoSettings.ColorTrc.Value = transfer;
        await Encode(controller);
        using MediaFrame frame = ReadFirstFrame(controller.OutputFile);
        Assert.Multiple(() =>
        {
            Assert.That(frame.ColorTrc, Is.EqualTo((AVColorTransferCharacteristic)(int)transfer));
            Assert.That(frame.ColorPrimaries, Is.EqualTo(AVColorPrimaries.AVCOL_PRI_BT2020));
            Assert.That(frame.Colorspace, Is.EqualTo(AVColorSpace.AVCOL_SPC_BT2020_NCL));
            Assert.That(frame.ColorRange, Is.EqualTo(AVColorRange.AVCOL_RANGE_MPEG));
            Assert.That(frame.Format, Is.EqualTo((int)AVPixelFormat.AV_PIX_FMT_YUV420P10LE));
        });
        // Preserve the existing HDR luminance mapping, then independently check Rec.2020 YUV coefficients/range.
        using Bitmap source = ColorBarsFrameProvider.CreateFrame();
        using Bitmap rgb = source.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul,
            ColorSpaceHelper.BuildHdrColorSpace((AVColorTransferCharacteristic)(int)transfer, AVColorPrimaries.AVCOL_PRI_BT2020));
        unsafe
        {
            ushort* row = (ushort*)((byte*)rgb.Data + 32 * rgb.RowBytes);
            for (int patch = 0; patch < 6; patch++)
            {
                int x = patch * 32 + 16;
                double r = row[x * 4] / 65535d, g = row[x * 4 + 1] / 65535d, b = row[x * 4 + 2] / 65535d;
                double y = 0.2627 * r + 0.6780 * g + 0.0593 * b;
                double[] expected = [64 + 876 * y, 512 + 896 * (b - y) / (2 * (1 - 0.0593)), 512 + 896 * (r - y) / (2 * (1 - 0.2627))];
                for (uint plane = 0; plane < 3; plane++)
                {
                    int divisor = plane == 0 ? 1 : 2;
                    ushort* actual = (ushort*)(frame.Data[plane] + (32 / divisor) * frame.Linesize[plane]);
                    Assert.That(actual[x / divisor], Is.EqualTo(expected[plane]).Within(3), $"Patch {patch}, plane {plane}");
                }
            }
        }
    }

    private FFmpegEncodingController CreateController(string name) => FFmpegTestHelpers.CreateController(
        Path.Combine(_directory, name), new PixelSize(ColorBarsFrameProvider.Width, ColorBarsFrameProvider.Height),
        new Rational(30, 1), 44100, 2);

    private static async Task Encode(FFmpegEncodingController controller)
    {
        using var frames = new ColorBarsFrameProvider();
        using var samples = new EmptySampleProvider();
        await controller.Encode(frames, samples, CancellationToken.None);
    }

    private static MediaFrame ReadFirstFrame(string path)
    {
        using var demuxer = MediaDemuxer.Open(path);
        MediaStream stream = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO);
        using var decoder = MediaDecoder.CreateDecoder(stream.CodecparRef);
        using var frame = new MediaFrame();
        using var packet = new MediaPacket();
        foreach (MediaPacket p in demuxer.ReadPackets(packet))
        {
            if (p.StreamIndex != stream.Index) continue;
            foreach (MediaFrame decoded in decoder.DecodePacket(p, frame))
                return decoded.Clone();
        }
        foreach (MediaFrame decoded in decoder.DecodePacket(null, frame))
            return decoded.Clone();
        throw new AssertionException("The encoded video has no frames.");
    }

    private static unsafe void AssertYuvPatches(MediaFrame frame, int[][] expected)
    {
        for (int patch = 0; patch < expected.Length; patch++)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int divisor = plane == 0 ? 1 : 2;
                int actual = frame.Data[(uint)plane][(32 / divisor) * frame.Linesize[(uint)plane] + (patch * 32 + 16) / divisor];
                Assert.That(actual, Is.EqualTo(expected[patch][plane]).Within(2), $"Patch {patch}, plane {plane}");
            }
        }
    }
}
