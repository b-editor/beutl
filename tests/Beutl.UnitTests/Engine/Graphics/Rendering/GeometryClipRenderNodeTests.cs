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
        node.HasChanges = false;

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
        node.HasChanges = false;

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(clip, ClipOperation.Difference), Is.True);
            Assert.That(node.HasChanges, Is.True);
        });
    }

    [Test]
    public void UnchangedReRecording_ShouldAdmitTheClipScopeToTheCache()
    {
        Geometry.Resource clip = CreateClip(30, 40);
        using var node = new GeometryClipRenderNode(clip, ClipOperation.Intersect);

        for (int frame = 0; frame < RenderNodeCache.Count; frame++)
        {
            node.Update(clip, ClipOperation.Intersect);
            node.Cache.IncrementRenderCount();
            node.HasChanges = false;
        }

        Assert.That(node.Cache.CanCache(), Is.True);
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
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
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
}
