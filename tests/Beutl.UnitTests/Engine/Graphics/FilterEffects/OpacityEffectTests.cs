using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class OpacityEffectTests
{
    private static readonly SKColor s_color = new(64, 128, 192, 160);

    [TestCase(100f)]
    [TestCase(float.NaN)]
    public void RecordsNothingForIdentityOrNaN(float opacity)
    {
        var effect = new OpacityEffect { Opacity = { Value = opacity } };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 1, 1));

        context.ApplyTransactional(resource);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [Test]
    public void RecordsOneCurrentPixelStage()
    {
        var effect = new OpacityEffect { Opacity = { Value = 50 } };
        var (description, bounds) = FilterEffectTestHarness.RecordShader(effect, new Rect(0, 0, 1, 1));

        Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
        Assert.That(bounds, Is.EqualTo(new Rect(0, 0, 1, 1)));
    }

    [Test]
    public void HalfOpacityHalvesEachPremultipliedChannel()
    {
        var effect = new OpacityEffect { Opacity = { Value = 50 } };
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(effect, s_color, new PixelSize(1, 1));

        Assert.That(after, Is.EqualTo(before.Select(channel => channel * 0.5f)).Within(0.001f));
    }

    [Test]
    public void ZeroOpacityMakesEveryChannelTransparent()
    {
        var effect = new OpacityEffect { Opacity = { Value = 0 } };
        (_, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(effect, s_color, new PixelSize(1, 1));

        Assert.That(after, Is.All.Zero);
    }
}
