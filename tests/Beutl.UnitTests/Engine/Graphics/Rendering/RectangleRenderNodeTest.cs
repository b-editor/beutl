using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RectangleRenderNodeTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void Update_ShouldNotMarkChanges_WhenAllPropertiesMatch(bool withPen)
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        Pen.Resource? pen = withPen ? CreatePen(thickness: 1) : null;
        using var node = new RectangleRenderNode(rect, fill, pen);
        node.ClearChanges(node.ChangeVersion);

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(rect, fill, pen), Is.False);
            Assert.That(node.HasChanges, Is.False);
        });
    }

    [Test]
    public void Update_ShouldMarkChanges_WhenPropertiesDoNotMatch()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(0, 0, 200, 200);
        var fill1 = Brushes.Resource.Red;
        var fill2 = Brushes.Resource.Blue;
        Pen.Resource penResource = CreatePen(thickness: 1);
        Pen.Resource otherPenResource = CreatePen(thickness: 2);
        using var node = new RectangleRenderNode(rect1, fill1, null);

        node.ClearChanges(node.ChangeVersion);
        bool rectChanged = node.Update(rect2, fill1, null);
        bool rectMarked = node.HasChanges;

        node.ClearChanges(node.ChangeVersion);
        bool fillChanged = node.Update(rect2, fill2, null);
        bool fillMarked = node.HasChanges;

        node.ClearChanges(node.ChangeVersion);
        bool penChanged = node.Update(rect2, fill2, penResource);
        bool penMarked = node.HasChanges;

        node.ClearChanges(node.ChangeVersion);
        bool otherPenChanged = node.Update(rect2, fill2, otherPenResource);
        bool otherPenMarked = node.HasChanges;

        Assert.Multiple(() =>
        {
            Assert.That(rectChanged, Is.True);
            Assert.That(rectMarked, Is.True);
            Assert.That(fillChanged, Is.True);
            Assert.That(fillMarked, Is.True);
            Assert.That(penChanged, Is.True);
            Assert.That(penMarked, Is.True);
            Assert.That(otherPenChanged, Is.True);
            Assert.That(otherPenMarked, Is.True);
        });
    }

    private static Pen.Resource CreatePen(float thickness)
    {
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = thickness;
        return pen.ToResource(CompositionContext.Default);
    }

    [Test]
    public void ChangedParameters_ShouldRevokeAnAdmittedCache()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        using var node = new RectangleRenderNode(rect, fill, null);

        for (int frame = 0; frame < RenderNodeCache.StableRequestCount; frame++)
        {
            node.Update(rect, fill, null);
            RenderNodeCacheHelper.BeginLifecycle(node).CompleteSuccessfully(advanceWarmup: true);
        }

        Assert.That(node.Cache.CanCapture, Is.True, "a stable rectangle must become a cache candidate");

        node.Update(new Rect(0, 0, 200, 200), fill, null);
        RenderNodeCacheHelper.BeginLifecycle(node);

        Assert.That(node.Cache.CanCapture, Is.False);
    }

    [Test]
    public void Measure_ShouldReportRecordedFragment()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(rect, fill, penResource);
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
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideRectangle()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(rect, fill, penResource);
        using var renderer = CreateRenderer(node);
        var point = new Point(50, 50);

        Assert.That(renderer.HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideRectangle()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(rect, fill, penResource);
        using var renderer = CreateRenderer(node);
        var point = new Point(150, 150);

        Assert.That(renderer.HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideRectangleStroke()
    {
        var rect = new Rect(25, 25, 75, 75);
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 50;
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(rect, null, penResource);
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
