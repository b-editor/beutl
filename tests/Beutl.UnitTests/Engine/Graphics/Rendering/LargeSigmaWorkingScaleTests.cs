using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class LargeSigmaWorkingScaleTests
{
    [Test]
    public void LargeSigmaBlur_CapsDeviceSigmaAtSafeCeiling()
    {
        using FilterEffect.Resource resource = CreateEffect("Blur", sigma: 250f);
        using FilterEffectRenderNode node = resource.CreateRenderNode();

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            node,
            EffectiveScale.At(4f),
            outputScale: 4f);

        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(500f / 250f).Within(0.0001f));
    }

    [Test]
    public void LargeSigmaDropShadow_KeepsSubjectAtStandardWorkingScale()
    {
        using FilterEffect.Resource resource = CreateEffect("DropShadow", sigma: 250f);
        using FilterEffectRenderNode node = resource.CreateRenderNode();

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            node,
            EffectiveScale.At(4f),
            outputScale: 4f);

        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(4f).Within(0.0001f));
    }

    [TestCase("Blur")]
    [TestCase("DropShadow")]
    public void SigmaBelowDeviceCeiling_KeepsStandardWorkingScale(string effectName)
    {
        using FilterEffect.Resource resource = CreateEffect(effectName, sigma: 100f);
        using FilterEffectRenderNode node = resource.CreateRenderNode();

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            node,
            EffectiveScale.At(4f),
            outputScale: 4f);

        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(4f).Within(0.0001f));
    }

    private static FilterEffect.Resource CreateEffect(string effectName, float sigma)
    {
        FilterEffect effect = effectName switch
        {
            "Blur" => new Blur
            {
                Sigma = { CurrentValue = new Size(sigma, sigma) }
            },
            "DropShadow" => new DropShadow
            {
                Sigma = { CurrentValue = new Size(sigma, sigma) },
                Color = { CurrentValue = Colors.White },
                ShadowOnly = { CurrentValue = true }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(effectName), effectName, null)
        };
        return effect.ToResource(CompositionContext.Default);
    }
}
