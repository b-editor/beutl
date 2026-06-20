using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFDecoderInfoTests
{
    private static IDecoderInfo CreateDecoderInfo() => new MFDecoderInfo(new MFDecodingExtension());

    [Test]
    public void VideoExtensions_IncludeCommonContainers()
    {
        string[] extensions = CreateDecoderInfo().VideoExtensions().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(extensions, Does.Contain(".mp4"));
            Assert.That(extensions, Does.Contain(".mov"));
            Assert.That(extensions, Does.Contain(".avi"));
        });
    }

    [Test]
    public void AudioExtensions_IncludeCommonFormats()
    {
        string[] extensions = CreateDecoderInfo().AudioExtensions().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(extensions, Does.Contain(".mp3"));
            Assert.That(extensions, Does.Contain(".wav"));
            Assert.That(extensions, Does.Contain(".m4a"));
        });
    }

    [TestCase("movie.mp4", true)]
    [TestCase("MOVIE.MP4", true)]
    [TestCase("song.mp3", true)]
    [TestCase("clip.mov", true)]
    [TestCase("document.xyz", false)]
    [TestCase("noextension", false)]
    public void IsSupported_MatchesByExtensionCaseInsensitively(string file, bool expected)
        => Assert.That(CreateDecoderInfo().IsSupported(file), Is.EqualTo(expected));

    [Test]
    public void Name_IsNotEmpty()
        => Assert.That(CreateDecoderInfo().Name, Is.Not.Empty);
}
