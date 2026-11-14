using Beutl.FFmpegWorker.Decoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

// The reader turns limited-range video into BGRA by the video's matrix and range, rounded to the nearest code value, as
// AVFoundation does. Libavfilter's default scaler came out up to 2 below it, white at 253 (#4287).
[TestFixture]
public sealed class FFmpegReaderYuvConversionTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        FFmpegTestHelpers.RequireFFmpeg();
        _directory = Path.Combine(Path.GetTempPath(), "beutl-yuv-conversion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (_directory != null) Directory.Delete(_directory, recursive: true);
    }

    // The gray steps of a screen recording in #4287, from black to white. With neutral chroma R, G and B all take
    // (Y - 16) * 255 / 219. Nine 18-unit patches make a width that is not a multiple of 16, which arm64's NEON path
    // leaves to the C one, 2 low; nine 16-unit ones a width it takes, which is exact there and up to 1 low on x64.
    [TestCase(18)]
    [TestCase(16)]
    public void Limited_range_gray_decodes_to_the_nearest_code_value(int patch)
    {
        byte[] lumas = [16, 23, 28, 34, 46, 64, 116, 174, 235];
        string path = Path.Combine(_directory, "gray.mkv");
        WriteLumaPatches(path, lumas, patch);

        using var reader = new FFmpegReader(path, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());
        Assert.That(reader.ReadVideo(0, out Ref<Bitmap>? image), Is.True);
        using (image)
        {
            Bitmap bitmap = image!.Value;
            var decoded = new List<string>();
            for (int i = 0; i < lumas.Length; i++)
            {
                Span<byte> pixel = bitmap.GetRow(patch / 2).Slice(((i * patch) + (patch / 2)) * 4, 4);
                decoded.Add($"Y={lumas[i]}: B={pixel[0]} G={pixel[1]} R={pixel[2]}");
            }

            TestContext.Out.WriteLine(string.Join(Environment.NewLine, decoded));
            Assert.Multiple(() =>
            {
                for (int i = 0; i < lumas.Length; i++)
                {
                    Span<byte> pixel = bitmap.GetRow(patch / 2).Slice(((i * patch) + (patch / 2)) * 4, 4);
                    int expected = (int)Math.Round((lumas[i] - 16) * 255.0 / 219);
                    Assert.That(new int[] { pixel[0], pixel[1], pixel[2] }, Is.All.EqualTo(expected).Within(1), decoded[i]);
                }
            });
        }
    }

    // One FFV1 frame of square patches in yuv420p, each with its Y and neutral chroma, tagged BT.709 in limited range as
    // a screen recording is. FFV1 keeps every sample, so only the reader's conversion decides the BGRA.
    private static unsafe void WriteLumaPatches(string path, byte[] lumas, int patch)
    {
        const AVPixelFormat format = AVPixelFormat.AV_PIX_FMT_YUV420P;
        MediaCodec? codec = MediaCodec.FindEncoder("ffv1");
        if (codec == null || !codec.GetPixelFmts().Contains(format))
            Assert.Ignore("The FFV1 encoder does not support yuv420p.");

        int width = patch * lumas.Length;
        using var stream = File.Create(path);
        using var muxer = MediaMuxer.Create(stream, OutputFormat.GuessFormat(null, path, null));
        using var encoder = MediaEncoder.Create(codec, context =>
        {
            context.Width = width;
            context.Height = patch;
            context.TimeBase = new AVRational { num = 1, den = 30 };
            context.Framerate = new AVRational { num = 30, den = 1 };
            context.PixFmt = format;
            context.ColorPrimaries = AVColorPrimaries.AVCOL_PRI_BT709;
            context.ColorTrc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
            context.Colorspace = AVColorSpace.AVCOL_SPC_BT709;
            context.ColorRange = AVColorRange.AVCOL_RANGE_MPEG;
        });
        MediaStream video = muxer.AddStream(encoder);
        muxer.WriteHeader();

        using var frame = new MediaFrame
        {
            Width = width,
            Height = patch,
            Format = (int)format,
            ColorPrimaries = AVColorPrimaries.AVCOL_PRI_BT709,
            ColorTrc = AVColorTransferCharacteristic.AVCOL_TRC_BT709,
            Colorspace = AVColorSpace.AVCOL_SPC_BT709,
            ColorRange = AVColorRange.AVCOL_RANGE_MPEG,
            Pts = 0,
        };
        frame.AllocateBuffer();
        for (int y = 0; y < patch; y++)
        {
            for (int x = 0; x < width; x++)
                frame.Data[0][(y * frame.Linesize[0]) + x] = lumas[x / patch];
        }

        for (int y = 0; y < patch / 2; y++)
        {
            for (int x = 0; x < width / 2; x++)
            {
                frame.Data[1][(y * frame.Linesize[1]) + x] = 128;
                frame.Data[2][(y * frame.Linesize[2]) + x] = 128;
            }
        }

        Write(encoder.EncodeFrame(frame));
        Write(encoder.EncodeFrame(null));
        muxer.WriteTrailer();

        void Write(IEnumerable<MediaPacket> packets)
        {
            foreach (MediaPacket packet in packets)
            {
                packet.StreamIndex = video.Index;
                packet.Duration = 1;
                ffmpeg.av_packet_rescale_ts(packet, encoder.TimeBase, video.TimeBase);
                muxer.WritePacket(packet).ThrowIfError();
            }
        }
    }
}
