using Beutl.Media.Decoding;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Engine.Media;

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
}
