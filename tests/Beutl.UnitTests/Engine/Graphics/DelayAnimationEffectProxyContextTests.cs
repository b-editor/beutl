using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

// DelayAnimationEffect re-applies its child effect through delayed CompositionContext instances. It
// must carry the proxy-selection flags into those contexts, or a video source inside a delayed child
// NodeGraphFilterEffect would decode originals while the rest of a prefer-proxy preview uses proxies.
[TestFixture]
public sealed class DelayAnimationEffectProxyContextTests
{
    [Test]
    public void DelayedTextBrushKeepsHostConditionsAcrossReconciliations()
    {
        var text = new TextBlock { Text = { Value = "Delayed text" } };
        var blend = new BlendEffect { Brush = { Value = new DrawableBrush(text) } };
        var delay = new DelayAnimationEffect { Effect = { Value = blend } };
        var composition = new CompositionContext(MediaTime.FromSeconds(1))
        {
            Fonts = TestFonts.Manager,
            FrameRate = new Rational(24, 1),
            TargetDomain = new Rect(0, 0, 200, 100),
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
        };
        using var resource = (DelayAnimationEffect.Resource)delay.ToResource(composition);
        using var targets = new EffectTargets { new EffectTarget() };
        using (var effectContext = new FilterEffectContext(new Rect(0, 0, 200, 100)))
        {
            effectContext.ApplyTransactional(resource);
            ((IFEItem_Custom)effectContext.GetOrderedItems().Single()).Accepts(new CustomFilterEffectContext(
                targets, new(RenderIntent.Preview, RenderRequestPurpose.Auxiliary)));
        }
        var delayed = (BlendEffect.Resource)resource.DelayedResources.Single();
        Assert.That(delayed.EvaluationContext!.Conditions, Is.SameAs(composition.Conditions));
        var brush = (DrawableBrush.Resource)delayed.Brush!;
        var delayedText = (TextBlock.Resource)brush.Drawable!;
        Assert.That(() => delayedText.Measure(new Size(200, 100)), Throws.Nothing);

        var rectangle = new RectShape { Width = { Value = 200 }, Height = { Value = 100 }, FilterEffect = { Value = delay } };
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = RendererTestSupport.CreateCpuRenderer(200, 100);
            renderer.Render(RendererTestSupport.CreateFrame(new PixelSize(200, 100), rectangle));
            text.Text.Value = "Updated delayed text";
            renderer.Render(RendererTestSupport.CreateFrame(new PixelSize(200, 100), rectangle));
        });
    }

    [Test]
    public void CatalogChangesInvalidateDelayedTargetsWithoutATimeChange()
    {
        var configuration = new Beutl.Configuration.FontConfig();
        configuration.FontDirectories.Clear();
        var first = new FontManager(configuration);
        var second = new FontManager(configuration);
        var effect = new DelayAnimationEffect();
        var context = new CompositionContext(MediaTime.Zero) { Fonts = first };
        using var resource = (DelayAnimationEffect.Resource)effect.ToResource(context);
        int version = resource.Version;
        context.Fonts = second;
        bool changed = false;
        resource.Reconcile(effect, context, ref changed);
        Assert.That(resource.Version, Is.GreaterThan(version));
        version = resource.Version;
        second.ClearProjectFonts();
        resource.Reconcile(effect, context, ref changed);
        Assert.That(resource.Version, Is.GreaterThan(version));
    }

    [Test]
    public void Resource_CapturesProxyPreferencesFromContext()
    {
        var effect = new DelayAnimationEffect();
        var context = new CompositionContext(MediaTime.Zero)
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

        var resource = (DelayAnimationEffect.Resource)effect.ToResource(new CompositionContext(MediaTime.Zero));

        Assert.That(resource.PreferProxy, Is.False);
    }

    // A proxy-mode toggle must bump Version so RenderNodeCache invalidates; otherwise the delayed
    // sub-effect keeps replaying cached tiles decoded from the previous source selection.
    [Test]
    public void Resource_BumpsVersionWhenProxyPreferenceChanges()
    {
        var effect = new DelayAnimationEffect();
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(
            new CompositionContext(MediaTime.Zero) { PreferProxy = false });
        int before = resource.Version;

        bool versionBumped = false;
        resource.Reconcile(effect, new CompositionContext(MediaTime.Zero) { PreferProxy = true }, ref versionBumped);

        Assert.That(resource.Version, Is.GreaterThan(before));
    }
}
