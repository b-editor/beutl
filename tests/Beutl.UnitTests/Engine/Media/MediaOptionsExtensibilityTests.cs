using Beutl.Media.Decoding;
using Beutl.Media.Proxy;

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
    public void Default_DoesNotPreferProxy()
    {
        var options = new MediaOptions();
        Assert.Multiple(() =>
        {
            Assert.That(options.PreferProxy, Is.False);
            Assert.That(options.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Quarter));
        });
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
        var baseline = new MediaOptions(MediaMode.AudioVideo)
        {
            PreferredProxyPreset = ProxyPreset.Half,
        };
        var narrowed = baseline with { StreamsToLoad = MediaMode.Video, PreferProxy = true };
        Assert.That(narrowed.StreamsToLoad, Is.EqualTo(MediaMode.Video));
        Assert.That(narrowed.PreferProxy, Is.True);
        Assert.That(narrowed.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
        Assert.That(baseline.StreamsToLoad, Is.EqualTo(MediaMode.AudioVideo));
        Assert.That(baseline.PreferProxy, Is.False);
        Assert.That(baseline.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
    }
}
