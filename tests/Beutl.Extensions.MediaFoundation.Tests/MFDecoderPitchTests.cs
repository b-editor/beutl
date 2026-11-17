using System.Runtime.Versioning;

using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Embedding.MediaFoundation.Encoding;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

// At width 1080 the decoder's YUY2 rows are 2176 bytes apart, not 1080 × 2 = 2160. Copying the buffer in one go
// shifted every row 8 px further right than the one above it, which shears the picture without any error.
// Width 1920 does not show it: its pitch is exactly width × 2.
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MFDecoderPitchTests
{
    private const int Width = 1080;
    private const int Height = 64;
    private const int BarLeft = 256;
    private const int BarWidth = 16;

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-mf-pitch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task ReadVideo_WidthWithPaddedPitch_KeepsAVerticalBarAtTheSameXOnEveryRow()
    {
        var controller = new MFEncodingController(Path.Combine(_directory, "bar.mp4"));
        controller.VideoSettings.UseHardwareAcceleration.Value = false;
        controller.VideoSettings.SourceSize.Value = controller.VideoSettings.DestinationSize.Value =
            new PixelSize(Width, Height);
        using var frames = new VerticalBarFrameProvider();
        using var samples = new SineSampleProvider(0, 0);
        await controller.Encode(frames, samples, CancellationToken.None);

        using MediaReader? reader = new MFDecoderInfo(new MFDecodingExtension())
            .Open(controller.OutputFile, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(Width, Height)));
        Assert.That(reader.ReadVideo(0, out var decoded), Is.True);
        using (decoded)
        {
            Bitmap image = decoded!.Value;
            Assert.Multiple(() =>
            {
                // Before the fix the bar moved 8 px further right on every row.
                foreach (int row in new[] { 2, Height / 2, Height - 3 })
                {
                    (int first, int last) = FindBar(image, row);
                    Assert.That(first, Is.EqualTo(BarLeft).Within(1), $"row {row}, left edge");
                    Assert.That(last, Is.EqualTo(BarLeft + BarWidth - 1).Within(1), $"row {row}, right edge");
                }
            });
        }
    }

    [TestCase("row-marker-pal.mp4", 768)]
    [TestCase("row-marker-square.mp4", 720)]
    public void ReadVideo_PixelAspectRatio_PreservesRowMarkersAtDisplaySize(string fixture, int displayWidth)
    {
        const int storedWidth = 720;
        const int height = 576;
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var extension = new MFDecodingExtension();
        MFMediaInfo info = MFThread.Dispatcher.Invoke(() =>
        {
            using var decoder = new MFDecoder(path, extension);
            return decoder.GetMediaInfo();
        });
        Assert.That(info.StorageSize, Is.EqualTo(new PixelSize(storedWidth, height)));
        Assert.That(info.Yuy2BufferSize, Is.EqualTo(storedWidth * height * 2));

        using MediaReader? reader = new MFDecoderInfo(extension).Open(path, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(displayWidth, height)));
        Assert.That(reader.ReadVideo(0, out var decoded), Is.True);
        using (decoded)
        {
            Bitmap image = decoded!.Value;
            Assert.That(new PixelSize(image.Width, image.Height), Is.EqualTo(reader.VideoInfo.FrameSize));
            double scaleX = (double)displayWidth / storedWidth;
            for (int row = 0; row < height; row++)
            {
                // In the stored frame the white bar spans [60 + row, 90 + row).
                (int first, int last) = FindBar(image, row);
                Assert.That(first, Is.EqualTo((60 + row) * scaleX).Within(1), $"row {row}, left edge");
                Assert.That(last, Is.EqualTo((90 + row) * scaleX - 1).Within(1), $"row {row}, right edge");
            }
        }
    }

    // The first and last columns brighter than mid gray, or (-1, -1) when the row has none.
    private static (int First, int Last) FindBar(Bitmap image, int row)
    {
        ReadOnlySpan<byte> pixels = image.GetRow(row);
        int first = -1, last = -1;
        for (int x = 0; x < image.Width; x++)
        {
            if (pixels[x * 4 + 1] > 128)
            {
                if (first < 0)
                    first = x;
                last = x;
            }
        }

        return (first, last);
    }

    // One white vertical bar on black, the same on every row.
    private sealed class VerticalBarFrameProvider : IFrameProvider
    {
        private long _next;

        public long FrameCount => 1;

        public Rational FrameRate => new(30, 1);

        public ValueTask<Bitmap?> ReadNextFrame()
        {
            if (_next++ >= FrameCount)
                return ValueTask.FromResult<Bitmap?>(null);

            var bitmap = new Bitmap(Width, Height);
            for (int y = 0; y < Height; y++)
            {
                Span<byte> row = bitmap.GetRow(y);
                for (int x = 0; x < Width; x++)
                {
                    byte value = x is >= BarLeft and < BarLeft + BarWidth ? (byte)255 : (byte)0;
                    row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = value;
                    row[x * 4 + 3] = 255;
                }
            }

            return ValueTask.FromResult<Bitmap?>(bitmap);
        }

        public void Dispose()
        {
        }
    }
}
