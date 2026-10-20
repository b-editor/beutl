using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionTextWrapperTests
{
    [Test]
    public void Wrap_PrefersWordBoundary()
    {
        var constraints = new CaptionTextConstraints(maximumLineLength: 11, maximumLineCount: 2);

        string result = CaptionTextWrapper.Wrap("Hello world again", constraints);

        Assert.That(result, Is.EqualTo("Hello world\nagain"));
    }

    [Test]
    public void Wrap_LongUnbrokenText_HardWrapsWithoutTruncation()
    {
        var constraints = new CaptionTextConstraints(maximumLineLength: 3, maximumLineCount: 2);

        string result = CaptionTextWrapper.Wrap("日本語字幕", constraints);

        Assert.That(result, Is.EqualTo("日本語\n字幕"));
    }

    [Test]
    public void Wrap_DoesNotSplitSurrogatePairs()
    {
        var constraints = new CaptionTextConstraints(maximumLineLength: 3, maximumLineCount: 2);

        string result = CaptionTextWrapper.Wrap("A😀B😀C", constraints);

        Assert.That(result, Is.EqualTo("A😀B\n😀C"));
    }

    [Test]
    public void Wrap_NormalizesExplicitLineEndingsAndPreservesBlankLine()
    {
        var constraints = new CaptionTextConstraints(maximumLineLength: 20, maximumLineCount: 3);

        string result = CaptionTextWrapper.Wrap("first\r\n\rsecond", constraints);

        Assert.That(result, Is.EqualTo("first\n\nsecond"));
    }
}
