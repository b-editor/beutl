using Beutl.Media.Decoding;

namespace Beutl.UnitTests.Media;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class DecoderFileExtensionsTests
{
    private readonly DecoderRegistry _decoders = new(new());

    [Test]
    public void Classify_ReflectsARegisteredDecoder()
    {
        var decoder = new FakeDecoderInfo([".fakevid"], [".fakeaud"]);
        Assert.That(_decoders.FileExtensions.Classify("a.fakevid"), Is.EqualTo(MediaFileKind.None));

        _decoders.Register(decoder);
        Assert.Multiple(() =>
        {
            Assert.That(_decoders.FileExtensions.Classify("a.fakevid"), Is.EqualTo(MediaFileKind.Video));
            Assert.That(_decoders.FileExtensions.Classify("a.fakeaud"), Is.EqualTo(MediaFileKind.Audio));
            Assert.That(_decoders.FileExtensions.Classify("a.FAKEVID"), Is.EqualTo(MediaFileKind.Video));
        });
        _decoders.Unregister(decoder);

        Assert.That(_decoders.FileExtensions.Classify("a.fakevid"), Is.EqualTo(MediaFileKind.None));
    }

    // The animated-image decoders register .png / .gif / .webp as video extensions, so anything
    // that decodes a still frame has to see them as images first.
    [TestCase("a.png")]
    [TestCase("a.apng")]
    [TestCase("a.gif")]
    [TestCase("a.webp")]
    public void Classify_PrefersImageOverVideo(string file)
    {
        Assert.That(_decoders.FileExtensions.Classify(file), Is.EqualTo(MediaFileKind.Image));
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
        Assert.That(_decoders.FileExtensions.Classify(file), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("noextension")]
    [TestCase("a.unknownext")]
    public void Classify_UnknownFile_IsNone(string file)
    {
        Assert.That(_decoders.FileExtensions.Classify(file), Is.EqualTo(MediaFileKind.None));
    }

    // FFmpeg claims '.ts' as a transport stream, which would otherwise hand every TypeScript source
    // file to a video decoder for thumbnails, media probes and the recursive media search.
    [Test]
    public void Classify_TypeScript_StaysNonMedia()
    {
        var decoder = new FakeDecoderInfo([".ts"], []);
        _decoders.Register(decoder);
        Assert.Multiple(() =>
        {
            Assert.That(_decoders.FileExtensions.Classify("app.ts"), Is.EqualTo(MediaFileKind.None));
            Assert.That(_decoders.FileExtensions.IsMedia("app.ts"), Is.False);
        });
    }

    // IDecoderInfo does not constrain the shape, and GetFilePatterns already accepts all three.
    // Classification and decoder selection have to agree, or the browser probes a file whose
    // decoder GuessDecoder never matches.
    [TestCase("mp4x")]
    [TestCase(".mp4x")]
    [TestCase("*.mp4x")]
    public void ClassifyAndDecoderSelection_AgreeOnTheShapeADecoderReturns(string declared)
    {
        var decoder = new FakeDecoderInfo([declared], []);
        _decoders.Register(decoder);
        Assert.Multiple(() =>
        {
            Assert.That(_decoders.FileExtensions.Classify("a.mp4x"), Is.EqualTo(MediaFileKind.Video));
            Assert.That(_decoders.GuessDecoder("a.mp4x"), Does.Contain(decoder));
        });
    }

    // The still-image set is Skia's own format table, so a format the image picker offers must not
    // be one the file browser calls unknown.
    [TestCase("a.heif")]
    [TestCase("a.dng")]
    [TestCase("a.avif")]
    [TestCase("a.wbmp")]
    public void Classify_KnowsEverySkiaStillImageFormat(string file)
    {
        Assert.That(_decoders.FileExtensions.Classify(file), Is.EqualTo(MediaFileKind.Image));
    }

    [Test]
    public void GetFilePatterns_NormalizesToGlobs()
    {
        var decoder = new FakeDecoderInfo([".fakevid"], []);
        _decoders.Register(decoder);
        Assert.That(
            _decoders.FileExtensions.GetFilePatterns(x => x.VideoExtensions()),
            Does.Contain("*.fakevid"));
    }

    private sealed class FakeDecoderInfo(string[] video, string[] audio) : IDecoderInfo
    {
        public string Name => "Fake Decoder";

        public MediaReader? Open(string file, MediaOptions options) => null;

        public IEnumerable<string> VideoExtensions() => video;

        public IEnumerable<string> AudioExtensions() => audio;
    }
}
