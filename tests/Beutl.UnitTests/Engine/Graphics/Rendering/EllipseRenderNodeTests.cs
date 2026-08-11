using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class EllipseRenderNodeTest
{
    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(CompositionContext.Default);
        var penResource = pen.ToResource(CompositionContext.Default);

        var node = new EllipseRenderNode(rect, fillResource, penResource);

        Assert.That(node.Update(rect, fillResource, penResource), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(0, 0, 200, 200);
        Brush fill1 = new SolidColorBrush(Colors.Red);
        Brush fill2 = new SolidColorBrush(Colors.Blue);
        Pen pen1 = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        Pen pen2 = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 2 } };
        var fillResource1 = fill1.ToResource(CompositionContext.Default);
        var fillResource2 = fill2.ToResource(CompositionContext.Default);
        var penResource1 = pen1.ToResource(CompositionContext.Default);
        var penResource2 = pen2.ToResource(CompositionContext.Default);

        var node = new EllipseRenderNode(rect1, fillResource1, penResource1);

        Assert.That(node.Update(rect2, fillResource1, penResource1), Is.True);
        Assert.That(node.Update(rect1, fillResource2, penResource1), Is.True);
        Assert.That(node.Update(rect1, fillResource1, penResource2), Is.True);
    }

    [Test]
    public void Update_ShouldNotMarkChanges_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fillResource = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fillResource, null);
        node.HasChanges = false;

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(rect, fillResource, null), Is.False);
            Assert.That(node.HasChanges, Is.False);
        });
    }

    [Test]
    public void Update_ShouldMarkChanges_WhenPropertiesDoNotMatch()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(0, 0, 200, 200);
        var fillResource1 = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        var fillResource2 = new SolidColorBrush(Colors.Blue).ToResource(CompositionContext.Default);
        Pen pen = new() { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect1, fillResource1, null);

        node.HasChanges = false;
        bool rectChanged = node.Update(rect2, fillResource1, null);
        bool rectMarked = node.HasChanges;

        node.HasChanges = false;
        bool fillChanged = node.Update(rect2, fillResource2, null);
        bool fillMarked = node.HasChanges;

        node.HasChanges = false;
        bool penChanged = node.Update(rect2, fillResource2, penResource);
        bool penMarked = node.HasChanges;

        Assert.Multiple(() =>
        {
            Assert.That(rectChanged, Is.True);
            Assert.That(rectMarked, Is.True);
            Assert.That(fillChanged, Is.True);
            Assert.That(fillMarked, Is.True);
            Assert.That(penChanged, Is.True);
            Assert.That(penMarked, Is.True);
        });
    }

    [Test]
    public void ChangedParameters_ShouldRevokeAnAdmittedCache()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fillResource = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fillResource, null);

        for (int frame = 0; frame < RenderNodeCache.StableRequestCount; frame++)
        {
            node.Update(rect, fillResource, null);
            RenderNodeCacheHelper.BeginLifecycle(node).CompleteSuccessfully(advanceWarmup: true);
        }

        Assert.That(node.Cache.CanCapture, Is.True, "a stable ellipse must become a cache candidate");

        node.Update(new Rect(0, 0, 200, 200), fillResource, null);
        RenderNodeCacheHelper.BeginLifecycle(node);

        Assert.That(node.Cache.CanCapture, Is.False);
    }

    [Test]
    public void Measure_ShouldReportRecordedFragment()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(CompositionContext.Default);
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fillResource, penResource);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
        });
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideEllipse()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(CompositionContext.Default);
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fillResource, penResource);
        using var renderer = CreateRenderer(node);
        var point = new Point(50, 50);

        Assert.That(renderer.HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideEllipse()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(CompositionContext.Default);
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fillResource, penResource);
        using var renderer = CreateRenderer(node);
        var point = new Point(150, 150);

        Assert.That(renderer.HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideEllipseStroke()
    {
        var rect = new Rect(25, 25, 75, 75);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 50 } };
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, null, penResource);
        using var renderer = CreateRenderer(node);
        var point = new Point(30, 50);

        Assert.That(renderer.HitTest(point), Is.True);
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });
}
