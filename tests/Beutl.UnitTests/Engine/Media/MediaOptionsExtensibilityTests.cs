using Beutl.Media.Decoding;

namespace Beutl.UnitTests.Engine.Media;

// Feature 003 (T048): MediaOptions must stay additively extensible so a future decode-scale hint
// can be added without changing today's behavior.
[TestFixture]
public class MediaOptionsExtensibilityTests
{
    [Test]
    public void Default_LoadsAudioVideo()
    {
        var options = new MediaOptions();
        Assert.That(options.StreamsToLoad, Is.EqualTo(MediaMode.AudioVideo));
    }

    [Test]
    public void StreamsToLoad_RoundTrips()
    {
        Assert.That(new MediaOptions(MediaMode.Video).StreamsToLoad, Is.EqualTo(MediaMode.Video));
        Assert.That(new MediaOptions(MediaMode.Audio).StreamsToLoad, Is.EqualTo(MediaMode.Audio));
    }

    [Test]
    public void RecordValueEquality_Holds()
    {
        Assert.That(new MediaOptions(MediaMode.Video), Is.EqualTo(new MediaOptions(MediaMode.Video)));
        Assert.That(new MediaOptions(MediaMode.Video), Is.Not.EqualTo(new MediaOptions(MediaMode.Audio)));
    }

    [Test]
    public void WithExpression_PreservesOtherMembers()
    {
        // The `with` clone is how a future decode-scale hint gets set without disturbing existing members.
        var baseline = new MediaOptions(MediaMode.AudioVideo);
        var narrowed = baseline with { StreamsToLoad = MediaMode.Video };
        Assert.That(narrowed.StreamsToLoad, Is.EqualTo(MediaMode.Video));
        Assert.That(baseline.StreamsToLoad, Is.EqualTo(MediaMode.AudioVideo));
    }
}
