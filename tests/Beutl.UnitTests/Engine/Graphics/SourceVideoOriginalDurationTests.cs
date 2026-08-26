using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

// Regression: TryGetOriginalDuration must return false (not a non-positive duration) once the
// offset reaches/passes the media end — matching the SourceSound positive guard.
[TestFixture]
public class SourceVideoOriginalDurationTests
{
    private static readonly TimeSpan MediaDuration = TimeSpan.FromSeconds(2);

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    private static SourceVideo CreateSourceVideo()
    {
        // 60 frames at 30 fps: a two-second test video decoded by the registered test decoder.
        string path = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        var video = new SourceVideo();
        video.Source.CurrentValue = source;
        return video;
    }

    [Test]
    public void TryGetOriginalDuration_OffsetInsideMedia_ReturnsRemainingDuration()
    {
        SourceVideo video = CreateSourceVideo();
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(0.5);

        Assert.That(video.TryGetOriginalDuration(out TimeSpan duration), Is.True);
        Assert.That(duration, Is.EqualTo(MediaDuration - TimeSpan.FromSeconds(0.5)));
    }

    [Test]
    public void TryGetOriginalDuration_OffsetAtMediaEnd_ReturnsFalse()
    {
        SourceVideo video = CreateSourceVideo();
        video.OffsetPosition.CurrentValue = MediaDuration;

        Assert.That(video.TryGetOriginalDuration(out TimeSpan duration), Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryGetOriginalDuration_OffsetPastMediaEnd_ReturnsFalse()
    {
        SourceVideo video = CreateSourceVideo();
        video.OffsetPosition.CurrentValue = MediaDuration + TimeSpan.FromSeconds(1);

        Assert.That(video.TryGetOriginalDuration(out TimeSpan duration), Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryGetOriginalDuration_AppliesSourceOffsetBeforeSpeedConversion()
    {
        SourceVideo video = CreateSourceVideo();
        video.Speed.CurrentValue = 200f;
        video.OffsetPosition.CurrentValue = TimeSpan.FromSeconds(0.5);

        Assert.That(video.TryGetOriginalDuration(out TimeSpan duration), Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromSeconds(0.75)).Within(TimeSpan.FromMilliseconds(1)));
    }
}
