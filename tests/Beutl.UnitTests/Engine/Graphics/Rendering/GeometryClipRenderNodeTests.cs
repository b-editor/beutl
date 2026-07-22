using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class GeometryClipRenderNodeTests
{
    [Test]
    public void Intersect_ClipsOutputBoundsAndHitTesting()
    {
        var geometry = new RectGeometry
        {
            Width = { CurrentValue = 30 },
            Height = { CurrentValue = 40 },
        };
        Geometry.Resource resource = geometry.ToResource(CompositionContext.Default);
        using var node = new GeometryClipRenderNode(resource, ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions { UseRenderCache = false });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 30, 40)));
            Assert.That(measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 30, 40)));
            Assert.That(renderer.HitTest(new Point(20, 20)), Is.True);
            Assert.That(renderer.HitTest(new Point(50, 20)), Is.False);
        });
    }
}
