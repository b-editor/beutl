using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

// TryGetOriginalDuration returns false when OffsetPosition is at or past the media end. It used to return true
// with a non-positive length there, which crashed Scene.MoveChild.
[TestFixture]
public class SourceVideoOriginalDurationTests
{
    private static readonly MediaTime MediaDuration = MediaTime.FromSeconds(2);

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    private static SourceVideo CreateSourceVideo()
    {
        // 60 frames at 30 fps: a two-second test video decoded by the registered test decoder.
        string path = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        var video = new SourceVideo();
        video.Source.Value = source;
        return video;
    }

    [Test]
    public void TryGetOriginalDuration_OffsetInsideMedia_ReturnsRemainingDuration()
    {
        SourceVideo video = CreateSourceVideo();
        video.OffsetPosition.Value = MediaTime.FromSeconds(0.5);

        Assert.That(video.TryGetOriginalDuration(out MediaTime duration), Is.True);
        Assert.That(duration, Is.EqualTo(MediaDuration - MediaTime.FromSeconds(0.5)));
    }

    [TestCase(0.0, TestName = "TryGetOriginalDuration_OffsetAtMediaEnd_ReturnsFalse")]
    [TestCase(1.0, TestName = "TryGetOriginalDuration_OffsetPastMediaEnd_ReturnsFalse")]
    public void TryGetOriginalDuration_OffsetAtOrPastMediaEnd_ReturnsFalse(double secondsPastEnd)
    {
        SourceVideo video = CreateSourceVideo();
        video.OffsetPosition.Value = MediaDuration + MediaTime.FromSeconds(secondsPastEnd);

        Assert.That(video.TryGetOriginalDuration(out MediaTime duration), Is.False);
        Assert.That(duration, Is.EqualTo(MediaTime.Zero));
    }
}
