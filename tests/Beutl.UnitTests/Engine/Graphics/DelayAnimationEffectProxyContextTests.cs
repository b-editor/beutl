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

    // A proxy-mode toggle must bump Version so RenderNodeCache invalidates; otherwise the delayed
    // sub-effect keeps replaying cached tiles decoded from the previous source selection.
    [Test]
    public void Resource_BumpsVersionWhenProxyPreferenceChanges()
    {
        var effect = new DelayAnimationEffect();
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(
            new CompositionContext(TimeSpan.Zero) { PreferProxy = false });
        int before = resource.Version;

        bool updateOnly = false;
        resource.Update(effect, new CompositionContext(TimeSpan.Zero) { PreferProxy = true }, ref updateOnly);

        Assert.That(resource.Version, Is.GreaterThan(before));
    }

    // DisableResourceShare selects isolated media readers for delayed child resources. A toggle must invalidate the
    // delayed render-node cache even when time and proxy selection are unchanged.
    [Test]
    public void Resource_BumpsVersionWhenResourceSharingChanges()
    {
        var effect = new DelayAnimationEffect();
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = false });
        int before = resource.Version;

        bool updateOnly = false;
        resource.Update(
            effect,
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true },
            ref updateOnly);

        Assert.That(resource.Version, Is.GreaterThan(before));
    }
}
