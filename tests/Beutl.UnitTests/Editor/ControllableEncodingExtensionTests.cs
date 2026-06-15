using Beutl.Extensibility;

namespace Beutl.UnitTests.Editor;

public class ControllableEncodingExtensionTests
{
    // The default IsSupported must match on extension regardless of the on-disk casing,
    // mirroring the (now removed) IEncoderInfo.IsSupported which used StringComparer.OrdinalIgnoreCase.
    [TestCase("video.mp4", ExpectedResult = true)]
    [TestCase("video.MP4", ExpectedResult = true)]
    [TestCase("video.Mp4", ExpectedResult = true)]
    [TestCase("clip.mov", ExpectedResult = true)]
    [TestCase("clip.MOV", ExpectedResult = true)]
    [TestCase("archive.mkv", ExpectedResult = false)]
    [TestCase("archive.MKV", ExpectedResult = false)]
    [TestCase("noextension", ExpectedResult = false)]
    public bool IsSupported_MatchesExtensionCaseInsensitively(string file)
    {
        var extension = new TestEncodingExtension();
        return extension.IsSupported(file);
    }

    private sealed class TestEncodingExtension : ControllableEncodingExtension
    {
        public override IEnumerable<string> SupportExtensions()
        {
            yield return ".mp4";
            yield return ".mov";
        }

        public override EncodingController CreateController(string file)
            => throw new NotSupportedException();
    }
}
