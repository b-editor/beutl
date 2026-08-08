using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media.Geometry;

using Geometry = Beutl.Media.Geometry;

/// <summary>
/// A "detached" resource is one built through its public parameterless constructor rather than through
/// <see cref="Beutl.Engine.EngineObject.ToResource"/>, so its backing engine object is null. At 989856e8d the
/// four public members a detached geometry resource needs — <c>Bounds</c>, <c>GetRenderBounds</c>,
/// <c>FillContains</c>, <c>StrokeContains</c> — were all non-virtual, so an out-of-tree author who built one
/// could not override the <see cref="NullReferenceException"/> away.
/// </summary>
/// <remarks>
/// Path construction now dispatches on the resource's own type, so a detached resource produces the same path
/// its attached counterpart does.
/// </remarks>
[TestFixture]
public sealed class DetachedGeometryResourceTests
{
    [Test]
    public void ADetachedEllipse_ProducesTheSameBoundsAsItsAttachedCounterpart()
    {
        using var detached = new EllipseGeometry.Resource { Width = 100, Height = 50 };
        using Geometry.Resource attached = new EllipseGeometry
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 50 },
        }.ToResource(CompositionContext.Default);

        Assert.That(detached.Bounds, Is.EqualTo(attached.Bounds));
    }

    [Test]
    public void ADetachedRect_ProducesTheSameBoundsAsItsAttachedCounterpart()
    {
        using var detached = new RectGeometry.Resource { Width = 30, Height = 40 };
        using Geometry.Resource attached = new RectGeometry
        {
            Width = { CurrentValue = 30 },
            Height = { CurrentValue = 40 },
        }.ToResource(CompositionContext.Default);

        Assert.That(detached.Bounds, Is.EqualTo(attached.Bounds));
    }

    [Test]
    public void EveryPublicEntryPointOfADetachedEllipse_Answers()
    {
        using var pen = new Pen
        {
            Brush = { CurrentValue = Brushes.Black },
            Thickness = { CurrentValue = 4 },
        }.ToResource(CompositionContext.Default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Fresh().Bounds, Is.EqualTo(new Rect(0, 0, 100, 50)));
            Assert.That(Fresh().GetRenderBounds(null), Is.EqualTo(new Rect(0, 0, 100, 50)));
            Assert.That(Fresh().GetRenderBounds(pen), Is.EqualTo(new Rect(-2, -2, 104, 54)));
            Assert.That(Fresh().FillContains(new Point(50, 25)), Is.True);
            Assert.That(Fresh().StrokeContains(null, new Point(0, 25)), Is.False);
            Assert.That(Fresh().StrokeContains(pen, new Point(0, 25)), Is.True);
        }

        static EllipseGeometry.Resource Fresh() => new() { Width = 100, Height = 50 };
    }

    [Test]
    public void ADetachedPathGeometryWithDetachedFiguresAndSegments_BuildsThatPath()
    {
        using var detached = new PathGeometry.Resource
        {
            Figures =
            {
                new PathFigure.Resource
                {
                    StartPoint = new Point(0, 0),
                    IsClosed = true,
                    Segments =
                    {
                        new LineSegment.Resource { Point = new Point(60, 0) },
                        new LineSegment.Resource { Point = new Point(60, 20) },
                    },
                },
            },
        };

        Assert.That(detached.Bounds, Is.EqualTo(new Rect(0, 0, 60, 20)));
    }

    [Test]
    public void AnAttachedPathGeometryHoldingADetachedFigure_BuildsThatFigure()
    {
        var geometry = new PathGeometry();
        geometry.Figures.Add(new PathFigure
        {
            StartPoint = { CurrentValue = new Point(0, 0) },
            Segments = { new LineSegment(new Point(10, 0)) },
        });
        using PathGeometry.Resource resource =
            (PathGeometry.Resource)geometry.ToResource(CompositionContext.Default);
        resource.Figures.Add(new PathFigure.Resource
        {
            StartPoint = new Point(0, 0),
            Segments = { new LineSegment.Resource { Point = new Point(80, 40) } },
        });
        resource.InvalidateCachedPaths();

        Assert.That(resource.Bounds, Is.EqualTo(new Rect(0, 0, 80, 40)));
    }

    [Test]
    public void GeometryRenderNode_WithADetachedGeometry_RecordsAndRasterizes()
    {
        using var detached = new EllipseGeometry.Resource { Width = 40, Height = 30 };
        using var node = new GeometryRenderNode(detached, Brushes.Resource.White, null);

        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            targetDomain: new Rect(0, 0, 64, 64),
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest { CacheOptions = RenderCacheOptions.Disabled },
            });
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.PublicationRoots.Count(), Is.EqualTo(1));
            Assert.That(rasterization.Bounds, Is.EqualTo(new Rect(0, 0, 40, 30)));
            Assert.That(rasterization.Bitmap, Is.Not.Null);
        }
    }

    [Test]
    public void GeometryClipRenderNode_WithADetachedGeometry_Measures()
    {
        using var detached = new RectGeometry.Resource { Width = 30, Height = 40 };
        using var node = new GeometryClipRenderNode(detached, ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest { CacheOptions = RenderCacheOptions.Disabled },
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 30, 40)));
    }
}
