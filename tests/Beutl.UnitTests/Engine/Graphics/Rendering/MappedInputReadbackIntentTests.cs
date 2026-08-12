using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// A mapped-input readback failure must decide degrade-vs-fail from the explicit
/// <see cref="RenderIntent"/>, not from the working-scale ceiling that happens to accompany it, and a
/// legacy custom shader effect must fail visibly when its replacement target never materialized.
/// </summary>
[TestFixture]
public sealed class MappedInputReadbackIntentTests
{
    [TestCase(float.PositiveInfinity)]
    [TestCase(2f)]
    public void DeliveryReadbackFailureFailsRegardlessOfTheWorkingScaleCeiling(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.That(
            () => CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(
                context.Intent,
                new PixelRect(0, 0, 4, 3)),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("4x3 px")
                .And.Message.Contains("delivery render fails"));
    }

    [TestCase(float.PositiveInfinity)]
    [TestCase(2f)]
    public void PreviewReadbackFailureDegradesRegardlessOfTheWorkingScaleCeiling(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.That(
            () => CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(
                context.Intent,
                new PixelRect(0, 0, 4, 3)),
            Throws.Nothing);
    }

    [Test]
    public void SkslScriptEffect_WithoutAMaterializedOutputTarget_FailsDescriptively()
    {
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue =
            """
            uniform shader src;

            half4 main(float2 fragCoord) {
                return src.eval(fragCoord);
            }
            """;
        var bounds = new Rect(0, 0, 4, 3);
        using var targets = new EffectTargets { new EffectTarget() };

        Assert.That(
            () => ApplyCustomDirect(effect, bounds, targets),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("no materialized output target"));
    }

    private static void ApplyCustomDirect(FilterEffect effect, Rect bounds, EffectTargets targets)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var recording = new FilterEffectContext(bounds, outputScale: 1f, workingScale: 1f);
        recording.ApplyTransactional(effect, resource);
        IFEItem_Custom item = recording.GetOrderedItems().OfType<IFEItem_Custom>().Single();
        var execution = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: 1f);
        item.Accepts(execution);
    }
}
