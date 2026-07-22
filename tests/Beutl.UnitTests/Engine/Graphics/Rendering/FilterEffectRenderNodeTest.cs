using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class FilterEffectRenderNodeTest
{
    private static FilterEffectRenderNode CreateNode(FilterEffect.Resource resource)
    {
        var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));
        return node;
    }

    [Test]
    public void Measure_ShouldReportRecordedFilterOutput()
    {
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);
        using var node = CreateNode(resource);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void Measure_ShouldApplyFilterEffectBounds()
    {
        var effect = new Blur() { Sigma = { CurrentValue = new(10, 10) } };
        var resource = effect.ToResource(CompositionContext.Default);
        using var node = CreateNode(resource);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.OutputBounds.X, Is.LessThan(0));
        Assert.That(measurement.OutputBounds.Y, Is.LessThan(0));
        Assert.That(measurement.OutputBounds.Width, Is.GreaterThan(100));
        Assert.That(measurement.OutputBounds.Height, Is.GreaterThan(100));
    }

    [Test]
    public void Update_ShouldReturnFalseForSameFilterEffect()
    {
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(resource);

        var result = node.Update(resource);

        Assert.That(result, Is.False);
    }

    // Updating an effect property changes its captured resource version.
    [Test]
    public void Update_ShouldReturnTrueForDifferentFilterEffectProperty()
    {
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(resource);
        effect.Sigma.CurrentValue = new(10, 10);
        var updateOnly = false;
        resource.Update(effect, CompositionContext.Default, ref updateOnly);

        var result = node.Update(resource);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Update_ShouldReturnTrueForDifferentFilterEffect()
    {
        var effect1 = new Blur();
        var effect2 = new DropShadow();
        var effectResource1 = effect1.ToResource(CompositionContext.Default);
        var effectResource2 = effect2.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(effectResource1);

        var result = node.Update(effectResource2);

        Assert.That(result, Is.True);
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRendererOptions { UseRenderCache = false });
}
