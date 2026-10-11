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

[TestFixture]
public sealed class EncodingStrideTests
{
    private string _workDir = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
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
    }

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-ffmpeg-stride-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_workDir, recursive: true);
    }

    [Test]
    public async Task LosslessEncodingPreservesEveryRow(
        [Values(64, 854, 1366)] int width, [Values] bool hdr)
    {
        AVPixelFormat encodedFormat = hdr ? AVPixelFormat.AV_PIX_FMT_GBRAP16LE : AVPixelFormat.AV_PIX_FMT_BGRA;
        MediaCodec? codec = MediaCodec.FindEncoder("ffv1");
        if (codec == null || !codec.GetPixelFmts().Contains(encodedFormat))
            Assert.Ignore($"The FFV1 encoder does not support {encodedFormat}.");

        const int height = 24;
        using (var frame = MediaFrame.CreateVideoFrame(
            width, height, hdr ? AVPixelFormat.AV_PIX_FMT_RGBA64LE : AVPixelFormat.AV_PIX_FMT_BGRA))
        {
            int packedRowBytes = width * (hdr ? 8 : 4);
            TestContext.WriteLine($"Input row: {packedRowBytes} bytes; FFmpeg stride: {frame.Linesize[0]} bytes.");
            Assert.That(frame.Linesize[0], width == 64 ? Is.EqualTo(packedRowBytes) : Is.GreaterThan(packedRowBytes));
        }

        string outputPath = Path.Combine(_workDir, "columns.mkv");
        var controller = new FFmpegEncodingController(outputPath, new FFmpegEncodingSettings());
        controller.VideoSettings.SourceSize = new PixelSize(width, height);
        controller.VideoSettings.DestinationSize = new PixelSize(width, height);
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
        using var frames = new ColumnFrameProvider(width, height);
        using var samples = new EmptySampleProvider();

        await controller.Encode(frames, samples, CancellationToken.None);

        using var bitmapReader = new FFmpegReader(outputPath, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());
        using var spanReader = new FFmpegReader(outputPath, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());
        Assert.That(bitmapReader.IsHdr, Is.EqualTo(hdr));
        int bytesPerPixel = hdr ? 8 : 4;
        var pixels = new byte[width * height * bytesPerPixel];
        BitmapColorSpace colorSpace = hdr
            ? ColorSpaceHelper.BuildHdrColorSpace(AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084, AVColorPrimaries.AVCOL_PRI_BT2020)
            : BitmapColorSpace.Srgb;

        using Bitmap source = await frames.RenderFrame(0);
        using Bitmap expected = source.Convert(
            hdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888,
            hdr ? BitmapAlphaType.Unpremul : BitmapAlphaType.Premul, colorSpace);
        Assert.That(bitmapReader.ReadVideo(0, out Ref<Bitmap>? decoded), Is.True);
        using (decoded)
        {
            Assert.That(decoded, Is.Not.Null);
            Bitmap actual = decoded!.Value;
            Assert.That(actual.Width, Is.EqualTo(width));
            Assert.That(actual.Height, Is.EqualTo(height));
            Assert.That(actual.BytesPerPixel, Is.EqualTo(bytesPerPixel));

            Array.Fill(pixels, (byte)0xCD);
            Assert.That(spanReader.ReadVideo(0, pixels.AsSpan(0, pixels.Length - 1), out VideoFrameInfo required), Is.False);
            Assert.That(required.DataLength, Is.EqualTo(pixels.Length));
            Assert.That(pixels, Is.All.EqualTo((byte)0xCD), "A short IPC buffer must remain untouched.");

            Assert.That(spanReader.ReadVideo(0, pixels, out VideoFrameInfo info), Is.True);
            Assert.That(info.DataLength, Is.EqualTo(pixels.Length));
            Assert.That(info.IsHdr, Is.EqualTo(hdr));

            for (int row = 0; row < height; row++)
            {
                Assert.That(expected.GetRow(row).SequenceEqual(actual.GetRow(row)), Is.True,
                    $"Bitmap row {row} differs from the rendered pixels.");
                Assert.That(expected.GetRow(row).SequenceEqual(pixels.AsSpan(row * width * bytesPerPixel, width * bytesPerPixel)), Is.True,
                    $"IPC row {row} differs from the rendered pixels.");
            }
        }
    }

    private sealed class ColumnFrameProvider(int width, int height) : IFrameProvider
    {
        public long FrameCount => 1;

        public Rational FrameRate => new(30, 1);

        public ValueTask<Bitmap> RenderFrame(long frame)
        {
            var bitmap = new Bitmap(width, height);
            for (int y = 0; y < height; y++)
            {
                Span<byte> row = bitmap.GetRow(y);
                for (int x = 0; x < width; x++)
                {
                    row[x * 4] = (byte)((x * 17) & 0xFF);
                    row[x * 4 + 1] = (byte)((y * 13) & 0xFF);
                    row[x * 4 + 2] = (byte)((x / 7 * 23 + y * 7) & 0xFF);
                    row[x * 4 + 3] = 255;
                }
            }

            return ValueTask.FromResult(bitmap);
        }

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
