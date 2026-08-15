using Beutl.Media.Decoding;

namespace Beutl.UnitTests.Media;

[TestFixture]
[NonParallelizable]
public class DecoderFileExtensionsTests
{
    [Test]
    public void Classify_ReflectsARegisteredDecoder()
    {
        var decoder = new FakeDecoderInfo([".fakevid"], [".fakeaud"]);
        Assert.That(DecoderFileExtensions.Classify("a.fakevid"), Is.EqualTo(MediaFileKind.None));

        DecoderRegistry.Register(decoder);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(DecoderFileExtensions.Classify("a.fakevid"), Is.EqualTo(MediaFileKind.Video));
                Assert.That(DecoderFileExtensions.Classify("a.fakeaud"), Is.EqualTo(MediaFileKind.Audio));
                Assert.That(DecoderFileExtensions.Classify("a.FAKEVID"), Is.EqualTo(MediaFileKind.Video));
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }

        Assert.That(DecoderFileExtensions.Classify("a.fakevid"), Is.EqualTo(MediaFileKind.None));
    }

    // The animated-image decoders register .png / .gif / .webp as video extensions, so anything
    // that decodes a still frame has to see them as images first.
    [TestCase("a.png")]
    [TestCase("a.apng")]
    [TestCase("a.gif")]
    [TestCase("a.webp")]
    public void Classify_PrefersImageOverVideo(string file)
    {
        Assert.That(DecoderFileExtensions.Classify(file), Is.EqualTo(MediaFileKind.Image));
    }

    // Every decoder for these containers ships as an optional extension, and .flac has none at all,
    // so without a baseline the file browser would stop recognising them as media.
    [TestCase("a.mp4", MediaFileKind.Video)]
    [TestCase("a.mkv", MediaFileKind.Video)]
    [TestCase("a.mov", MediaFileKind.Video)]
    [TestCase("a.flac", MediaFileKind.Audio)]
    [TestCase("a.mp3", MediaFileKind.Audio)]
    [TestCase("a.FLAC", MediaFileKind.Audio)]
    public void Classify_KnownContainer_DoesNotNeedARegisteredDecoder(string file, MediaFileKind expected)
    {
        Assert.That(DecoderFileExtensions.Classify(file), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("noextension")]
    [TestCase("a.unknownext")]
    public void Classify_UnknownFile_IsNone(string file)
    {
        Assert.That(DecoderFileExtensions.Classify(file), Is.EqualTo(MediaFileKind.None));
    }

    // The still-image set is Skia's own format table, so a format the image picker offers must not
    // be one the file browser calls unknown.
    [TestCase("a.heif")]
    [TestCase("a.dng")]
    [TestCase("a.avif")]
    [TestCase("a.wbmp")]
    public void Classify_KnowsEverySkiaStillImageFormat(string file)
    {
        Assert.That(DecoderFileExtensions.Classify(file), Is.EqualTo(MediaFileKind.Image));
    }

    [Test]
    public void GetFilePatterns_NormalizesToGlobs()
    {
        var decoder = new FakeDecoderInfo([".fakevid"], []);
        DecoderRegistry.Register(decoder);
        try
        {
            Assert.That(
                DecoderFileExtensions.GetFilePatterns(x => x.VideoExtensions()),
                Does.Contain("*.fakevid"));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    private sealed class FakeDecoderInfo(string[] video, string[] audio) : IDecoderInfo
    {
        public string Name => "Fake Decoder";

        public MediaReader? Open(string file, MediaOptions options) => null;

        public IEnumerable<string> VideoExtensions() => video;

        public IEnumerable<string> AudioExtensions() => audio;
    }
}
