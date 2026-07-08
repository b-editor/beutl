using Beutl.Composition;
using Beutl.Graphics.Effects;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Engine.Graphics;

// DelayAnimationEffect re-applies its child effect through delayed CompositionContext instances. It
// must carry the proxy-selection flags into those contexts, or a video source inside a delayed child
// NodeGraphFilterEffect would decode originals while the rest of a prefer-proxy preview uses proxies.
[TestFixture]
public sealed class DelayAnimationEffectProxyContextTests
{
    [Test]
    public void Resource_CapturesProxyPreferencesFromContext()
    {
        var effect = new DelayAnimationEffect();
        var context = new CompositionContext(TimeSpan.Zero)
        {
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
        };

        var resource = (DelayAnimationEffect.Resource)effect.ToResource(context);

        Assert.Multiple(() =>
        {
            Assert.That(resource.PreferProxy, Is.True);
            Assert.That(resource.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
        });
    }

    [Test]
    public void Resource_DefaultsToOriginalDecode()
    {
        var effect = new DelayAnimationEffect();

        var resource = (DelayAnimationEffect.Resource)effect.ToResource(new CompositionContext(TimeSpan.Zero));

        Assert.That(resource.PreferProxy, Is.False);
    }
}
