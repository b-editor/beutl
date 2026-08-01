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

    [Test]
    public void AnimatedBlurSigma_ReusesStructuralPlanAcrossWorkingScaleThreshold()
    {
        using FilterEffect.Resource first = CreateEffect("Blur", sigma: 100);
        using var node = first.CreateRenderNode();
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 64, 64),
            Brushes.Resource.White,
            pen: null));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 64, 64),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement firstMeasurement = renderer.Measure();
        using (renderer.Rasterize())
        {
        }

        using FilterEffect.Resource second = CreateEffect("Blur", sigma: 600);
        Assert.That(node.Update(second), Is.True);
        RenderNodeMeasurement secondMeasurement = renderer.Measure();
        using (renderer.Rasterize())
        {
        }

        StructuralPlanCacheStatistics statistics = renderer.StructuralPlanCacheStatistics;
        Assert.Multiple(() =>
        {
            Assert.That(firstMeasurement.EffectiveScale.Value, Is.EqualTo(1).Within(0.0001f));
            Assert.That(secondMeasurement.EffectiveScale.Value, Is.EqualTo(500f / 600f).Within(0.0001f));
            Assert.That(secondMeasurement.EffectiveScale, Is.Not.EqualTo(firstMeasurement.EffectiveScale));
            Assert.That(statistics.Compilations, Is.EqualTo(1));
            Assert.That(statistics.Misses, Is.EqualTo(1));
            Assert.That(statistics.Hits, Is.EqualTo(1));
            Assert.That(statistics.Replacements, Is.Zero);
        });
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
