using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.AVFoundation.Tests;

// An output of another aspect ratio than the scene, such as a 9:16 platform preset over a 16:9 scene, gets the whole
// frame between black bars instead of a stretched one. 256x144 into 144x256 leaves a picture about 81 rows tall in the
// middle; the rows checked keep clear of the edges, where the lossy codec blurs the picture into the bars.
[TestFixture]
[Platform("MacOSX")]
public sealed class LetterboxTests
{
    private const int SourceWidth = 256;
    private const int SourceHeight = 144;
    private const int Width = 144;
    private const int Height = 256;

    private string _workDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-avf-letterbox-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public async Task A9By16OutputOfA16By9Frame_HasTheWholeFrameBetweenBlackBars()
    {
        using Bitmap decoded = await EncodeAndDecode(hdr: false);

        Assert.That(decoded.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
        AssertRows(decoded, static pixel => pixel[0] <= 24 && pixel[1] <= 24 && pixel[2] <= 24,
            static pixel => Math.Abs(pixel[0] - 40) <= 24
                            && Math.Abs(pixel[1] - 160) <= 24
                            && Math.Abs(pixel[2] - 200) <= 24);
    }

    [Test]
    public async Task A9By16HdrOutputOfA16By9Frame_HasTheWholeFrameBetweenBlackBars()
    {
        using Bitmap decoded = await EncodeAndDecode(hdr: true);

        Assert.That(decoded.ColorType, Is.EqualTo(BitmapColorType.Rgba16161616));
        AssertRows(decoded, static pixel => Max16(pixel) <= 4096, static pixel => Max16(pixel) >= 8192);
    }

    private async Task<Bitmap> EncodeAndDecode(bool hdr)
    {
        string outputPath = Path.Combine(_workDir, "fitted.mp4");
        var controller = new AVFEncodingController(outputPath);
        controller.VideoSettings.SourceSize = new PixelSize(SourceWidth, SourceHeight);
        controller.VideoSettings.DestinationSize = new PixelSize(Width, Height);
        controller.VideoSettings.FrameRate = new Rational(30, 1);
        if (hdr)
        {
            controller.VideoSettings.ColorTransfer = AVFVideoEncoderSettings.ColorTransferCharacteristic.Pq;
            controller.VideoSettings.ColorPrimaries = AVFVideoEncoderSettings.ColorPrimariesType.Rec2020;
            controller.VideoSettings.YCbCrMatrix = AVFVideoEncoderSettings.YCbCrMatrixType.Rec2020;
        }

        controller.AudioSettings.SampleRate = 44100;
        controller.AudioSettings.Channels = 2;

        using var frames = new SolidFrameProvider(new PixelSize(SourceWidth, SourceHeight), frameCount: 3);
        using var samples = new SineSampleProvider(44100 / 10, 44100);
        await controller.Encode(frames, samples, CancellationToken.None);

        using var reader = new AVFReader(outputPath, new MediaOptions(), new AVFDecodingExtension());
        Assert.That(reader.ReadVideo(frame: 1, out var image), Is.True);
        using (image)
        {
            Assert.That((image!.Value.Width, image.Value.Height), Is.EqualTo((Width, Height)));
            return image.Value.Clone();
        }
    }

    private static void AssertRows(Bitmap decoded, PixelCheck isBar, PixelCheck isPicture)
    {
        foreach ((int start, int end, PixelCheck check, string what) in new[]
                 {
                     (0, 70, isBar, "a bar"),
                     (100, 156, isPicture, "the picture"),
                     (186, Height, isBar, "a bar"),
                 })
        {
            for (int y = start; y < end; y++)
            {
                ReadOnlySpan<byte> row = decoded.GetRow(y);
                for (int x = 0; x < decoded.Width; x++)
                {
                    ReadOnlySpan<byte> pixel = row.Slice(x * decoded.BytesPerPixel, decoded.BytesPerPixel);
                    Assert.That(check(pixel), Is.True, $"({x}, {y}) should be {what}.");
                }
            }
        }
    }

    private static int Max16(ReadOnlySpan<byte> pixel)
    {
        int max = 0;
        for (int channel = 0; channel < 3; channel++)
            max = Math.Max(max, pixel[channel * 2] | (pixel[channel * 2 + 1] << 8));

        return max;
    }

    private delegate bool PixelCheck(ReadOnlySpan<byte> pixel);
}
