using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class GeometryClipRenderNodeTests
{
    [Test]
    public void Update_ShouldNotMarkChanges_WhenAllPropertiesMatch()
    {
        Geometry.Resource clip = CreateClip(30, 40);
        using var node = new GeometryClipRenderNode(clip, ClipOperation.Intersect);
        node.ClearChanges(node.ChangeVersion);

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(clip, ClipOperation.Intersect), Is.False);
            Assert.That(node.HasChanges, Is.False);
        });
    }

    [Test]
    public void Update_ShouldMarkChanges_WhenPropertiesDoNotMatch()
    {
        Geometry.Resource clip = CreateClip(30, 40);
        using var node = new GeometryClipRenderNode(clip, ClipOperation.Intersect);
        node.ClearChanges(node.ChangeVersion);

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(clip, ClipOperation.Difference), Is.True);
            Assert.That(node.HasChanges, Is.True);
        });
    }


    private static Geometry.Resource CreateClip(float width, float height)
    {
        var geometry = new RectGeometry
        {
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
        };
        return geometry.ToResource(CompositionContext.Default);
    }

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
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 30, 40)));
            Assert.That(measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 30, 40)));
            Assert.That(renderer.HitTest(new Point(20, 20)), Is.True);
            Assert.That(renderer.HitTest(new Point(50, 20)), Is.False);
        });
    }

    [Test]
    public void HitTest_UsesTheUpdatedOperationWithoutRecompilingThePlan()
    {
        Geometry.Resource resource = CreateClip(30, 40);
        using var node = new GeometryClipRenderNode(resource, ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = RenderCacheOptions.Disabled,
        });

        renderer.Rasterize().Dispose();
        long compilations = renderer.StructuralPlanCacheStatistics.Compilations;
        Assert.Multiple(() =>
        {
            Assert.That(renderer.HitTest(new Point(20, 20)), Is.True);
            Assert.That(renderer.HitTest(new Point(50, 20)), Is.False);
        });

        Geometry.Resource updatedResource = CreateClip(60, 40);
        Assert.That(node.Update(updatedResource, ClipOperation.Difference), Is.True);
        renderer.Rasterize().Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(renderer.HitTest(new Point(20, 20)), Is.False);
            Assert.That(renderer.HitTest(new Point(50, 20)), Is.False);
            Assert.That(renderer.HitTest(new Point(80, 20)), Is.True);
            Assert.That(compilations, Is.GreaterThan(0));
            Assert.That(renderer.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(compilations));
        });
    }
}
