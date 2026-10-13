using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class EllipseRenderNodeTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void Update_ShouldNotMarkChanges_WhenAllPropertiesMatch(bool withPen)
    {
        var rect = new Rect(0, 0, 100, 100);
        var fillResource = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        Pen.Resource? penResource = withPen
            ? new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } }
                .ToResource(CompositionContext.Default)
            : null;
        using var node = new EllipseRenderNode(rect, fillResource, penResource);
        node.ClearChanges(node.ChangeVersion);

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(rect, fillResource, penResource), Is.False);
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
        Pen thickerPen = new() { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 2 } };
        var penResource = pen.ToResource(CompositionContext.Default);
        var thickerPenResource = thickerPen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect1, fillResource1, null);

        node.ClearChanges(node.ChangeVersion);
        bool rectChanged = node.Update(rect2, fillResource1, null);
        bool rectMarked = node.HasChanges;

        node.ClearChanges(node.ChangeVersion);
        bool fillChanged = node.Update(rect2, fillResource2, null);
        bool fillMarked = node.HasChanges;

        node.ClearChanges(node.ChangeVersion);
        bool penChanged = node.Update(rect2, fillResource2, penResource);
        bool penMarked = node.HasChanges;

        node.ClearChanges(node.ChangeVersion);
        bool penThicknessChanged = node.Update(rect2, fillResource2, thickerPenResource);
        bool penThicknessMarked = node.HasChanges;

        Assert.Multiple(() =>
        {
            Assert.That(rectChanged, Is.True);
            Assert.That(rectMarked, Is.True);
            Assert.That(fillChanged, Is.True);
            Assert.That(fillMarked, Is.True);
            Assert.That(penChanged, Is.True);
            Assert.That(penMarked, Is.True);
            Assert.That(penThicknessChanged, Is.True);
            Assert.That(penThicknessMarked, Is.True);
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

    [TestCase(50f, 50f, true)]
    [TestCase(150f, 150f, false)]
    public void HitTest_ShouldReturnWhetherPointIsInsideEllipse(float x, float y, bool expected)
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(CompositionContext.Default);
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(rect, fillResource, penResource);
        using var renderer = CreateRenderer(node);

        Assert.That(renderer.HitTest(new Point(x, y)), Is.EqualTo(expected));
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
        => new(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
}
