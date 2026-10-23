using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Decoding;

// Skia's RepetitionCount counts the repetitions after the first pass; a GIF without a loop extension plays once.
[TestFixture]
public class AnimatedImageReaderTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-animated-image-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void APlayOnceGif_AnimatesOnceAndThenShowsNothing()
    {
        using var reader = Open(CreateGif(loopCount: null));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPixel(reader, 0), Is.EqualTo(SKColors.Black));
            Assert.That(ReadPixel(reader, 1), Is.EqualTo(SKColors.White));
            Assert.That(reader.ReadVideo(2, out _), Is.False);
        });
    }

    [Test]
    public void AGifThatRepeatsOnce_PlaysTwoPasses()
    {
        using var reader = Open(CreateGif(loopCount: 1));

        Assert.Multiple(() =>
        {
            Assert.That(ReadPixel(reader, 2), Is.EqualTo(SKColors.Black));
            Assert.That(ReadPixel(reader, 3), Is.EqualTo(SKColors.White));
            Assert.That(reader.ReadVideo(4, out _), Is.False);
        });
    }

    [Test]
    public void AGifThatLoopsForever_KeepsAnimating()
    {
        using var reader = Open(CreateGif(loopCount: 0));

        Assert.That(ReadPixel(reader, 101), Is.EqualTo(SKColors.White));
    }

    private AnimatedImageReader Open(byte[] gif)
    {
        string file = Path.Combine(_directory, "two-frames.gif");
        File.WriteAllBytes(file, gif);
        return new AnimatedImageReader(file);
    }

    private static SKColor ReadPixel(AnimatedImageReader reader, int frame)
    {
        Assert.That(reader.ReadVideo(frame, out Ref<Bitmap>? image), Is.True, $"frame {frame}");
        using (image)
        {
            return image!.Value.SKBitmap.GetPixel(0, 0);
        }
    }

    // A black and then a white 1 x 1 frame of 100 ms each, so the reader runs at 10 frames per second. A loop
    // count adds the NETSCAPE2.0 extension; 0 there means to loop forever.
    private static byte[] CreateGif(ushort? loopCount)
    {
        byte[] loop = loopCount is { } count
            ? [0x21, 0xFF, 11, .. "NETSCAPE2.0"u8, 3, 1, (byte)count, (byte)(count >> 8), 0]
            : [];
        return
        [
            .. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF,
            .. loop,
            .. Frame(colorIndex: 0),
            .. Frame(colorIndex: 1),
            0x3B,
        ];
    }

    // The LZW data is a clear code, the color index and the end code.
    private static byte[] Frame(int colorIndex)
        => [0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, colorIndex == 0 ? (byte)0x44 : (byte)0x4C, 1, 0];
}
