using System.Text;
using Beutl.Media.Decoding;
using Beutl.Media.Decoding.APNG;
using Beutl.Media.Wave;
using NAudio.Wave;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Decoding;

// DecoderRegistry tries the next decoder only when Open returns null, so a decoder must not open a file for a stream it lacks.
[TestFixture]
public class BuiltInDecoderStreamTests
{
    private string _directory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-decoder-streams-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        using (var writer = new WaveFileWriter(Path.Combine(_directory, "silence.wav"), new WaveFormat(8000, 16, 1)))
        {
            writer.Write(new byte[1600], 0, 1600);
        }

        File.WriteAllBytes(Path.Combine(_directory, "two-frames.gif"), CreateGif());
        File.WriteAllBytes(Path.Combine(_directory, "one-frame.apng"), CreateApng());
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void Wave_OpensOnlyWhenAudioIsRequested()
    {
        AssertOpensOnlyFor(new WaveDecoderInfo(), "silence.wav", MediaMode.Audio, MediaMode.Video);
    }

    [Test]
    public void AnimatedImage_OpensOnlyWhenVideoIsRequested()
    {
        AssertOpensOnlyFor(new AnimatedImageDecoderInfo(), "two-frames.gif", MediaMode.Video, MediaMode.Audio);
    }

    [Test]
    public void AnimatedPng_OpensOnlyWhenVideoIsRequested()
    {
        AssertOpensOnlyFor(new AnimatedPngDecoderInfo(), "one-frame.apng", MediaMode.Video, MediaMode.Audio);
    }

    private void AssertOpensOnlyFor(IDecoderInfo decoder, string fileName, MediaMode stream, MediaMode otherStream)
    {
        string file = Path.Combine(_directory, fileName);
        using MediaReader? withStream = decoder.Open(file, new MediaOptions(stream));
        using MediaReader? withBoth = decoder.Open(file, new MediaOptions(MediaMode.AudioVideo));
        using MediaReader? withoutStream = decoder.Open(file, new MediaOptions(otherStream));

        Assert.Multiple(() =>
        {
            Assert.That(withStream, Is.Not.Null);
            Assert.That(withBoth, Is.Not.Null);
            Assert.That(withoutStream, Is.Null);
        });
    }

    // Two 1 x 1 frames of 100 ms each.
    private static byte[] CreateGif()
    {
        byte[] frame = [0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0];
        return [.. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, .. frame, .. frame, 0x3B];
    }

    // One 2 x 2 frame of 100 ms: a PNG with acTL and fcTL in front of its IDAT.
    private static byte[] CreateApng()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Red);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] png = data.ToArray();
        const int AfterIhdr = 8 + 25;
        byte[] acTL = [0, 0, 0, 1, 0, 0, 0, 0];
        byte[] fcTL = [0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 10, 0, 0];
        return [.. png.AsSpan(0, AfterIhdr), .. Chunk("acTL", acTL), .. Chunk("fcTL", fcTL), .. png.AsSpan(AfterIhdr)];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        uint crc = CrcHelper.Calculate([.. typeBytes, .. data]);
        return
        [
            0, 0, 0, (byte)data.Length, .. typeBytes, .. data,
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc,
        ];
    }
}
