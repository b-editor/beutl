using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

/// <summary>
/// Pins that a preview frame which dropped part of itself leaves nothing that outlives it.
/// </summary>
/// <remarks>
/// A preview degrades rather than fails when a target cannot be allocated, so the frame on screen is
/// deliberately incomplete. Anything that survives the frame - the persistent node cache above all - would
/// then keep serving those missing pixels long after the memory pressure that caused them is gone.
/// </remarks>
[TestFixture]
public sealed class DegradedPreviewCachePurityTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 24);

    [Test]
    public void APreviewWhoseBrushDroppedItsIntermediate_PublishesNothingToTheNodeCache()
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = (float)s_bounds.Width;
        shape.Height.CurrentValue = (float)s_bounds.Height;
        shape.Fill.CurrentValue = CreateDrawableBrush();
        using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_bounds.Size))
        {
            context.Clear();
            context.DrawDrawable(resource);
        }

        GeometryRenderNode cacheable = Descendants(root).OfType<GeometryRenderNode>().First();
        cacheable.Cache.RecordStableRequests();
        // The frame's own target is the only size this factory hands out, so the brush's own intermediate,
        // which is sized from the brush content, is declined.
        var factory = new SizedTargetFactory(PixelRect.FromRect(s_bounds, 1).Size);
        using RenderNodeRenderer renderer = CreateRenderer(root, factory, RenderIntent.Preview);

        renderer.Rasterize().Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(factory.Declined, Is.GreaterThan(0),
                "The fixture must actually make the brush run out of targets.");
            Assert.That(cacheable.Cache.IsCached, Is.False,
                "A frame whose brush degraded to transparent must not leave those pixels in the cache.");
        });
    }

    [Test]
    public void APreviewThatDroppedAnAllocation_CommitsNoBackdropSnapshot()
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = (float)s_bounds.Width;
        shape.Height.CurrentValue = (float)s_bounds.Height;
        shape.Fill.CurrentValue = CreateDrawableBrush();
        using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        using var brushRoot = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(brushRoot, s_bounds.Size))
        {
            context.Clear();
            context.DrawDrawable(resource);
        }

        using var root = new ContainerRenderNode();
        root.AddChild(brushRoot);
        var probe = new BackdropSinkProbeNode();
        root.AddChild(probe);
        var factory = new SizedTargetFactory(PixelRect.FromRect(s_bounds, 1).Size);
        using RenderNodeRenderer renderer = CreateRenderer(root, factory, RenderIntent.Preview);

        renderer.Rasterize().Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(factory.Declined, Is.GreaterThan(0),
                "The fixture must actually make the brush run out of targets.");
            Assert.That(probe.Commits, Is.Zero,
                "A snapshot sink outlives the frame, so a degraded frame has nothing fit to commit to it.");
        });
    }

    private sealed class BackdropSinkProbeNode : SnapshotBackdropRenderNode, IBuiltInBackdropCaptureSink
    {
        public int Commits { get; private set; }

        bool IBuiltInBackdropCaptureSink.TryCommitBackdropCapture(Bitmap bitmap, float density)
        {
            Commits++;
            bitmap.Dispose();
            return true;
        }

        void IBuiltInBackdropCaptureSink.CommitBackdropCapture(Bitmap bitmap, float density)
        {
            Commits++;
            bitmap.Dispose();
        }
    }

    /// <remarks>
    /// A nested request renders into its own target and the parent composites that target, so a drop the
    /// nested body observed makes the parent's output incomplete too. Reporting only a failed nested root
    /// acquisition left the parent free to publish those pixels.
    /// </remarks>
    [Test]
    public void APreviewWhoseNestedRequestDropped_PublishesNothingToTheParentsNodeCache()
    {
        using RenderNode nestedRoot = CreateBrushRoot(out Drawable.Resource nestedResource);
        using (nestedResource)
        {
            using var parent = new NestedTargetNode(nestedRoot, s_bounds);
            parent.Cache.RecordStableRequests();
            var factory = new SizedTargetFactory(PixelRect.FromRect(s_bounds, 1).Size);
            using RenderNodeRenderer renderer = CreateRenderer(parent, factory, RenderIntent.Preview);

            renderer.Rasterize().Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(factory.Declined, Is.GreaterThan(0),
                    "The fixture must actually make the nested brush run out of targets.");
                Assert.That(parent.Cache.IsCached, Is.False,
                    "A parent compositing a degraded nested request must not cache the result.");
            });
        }
    }

    private static RenderNode CreateBrushRoot(out Drawable.Resource resource)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = (float)s_bounds.Width;
        shape.Height.CurrentValue = (float)s_bounds.Height;
        shape.Fill.CurrentValue = CreateDrawableBrush();
        resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_bounds.Size))
        {
            context.Clear();
            context.DrawDrawable(resource);
        }

        return root;
    }

    private sealed class NestedTargetNode(RenderNode nestedRoot, Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            _ = context.RecordNestedTarget(nestedRoot, bounds);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                bounds,
                static (session, area) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(area);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.ContributeValues(context.OpaqueSource(description)));
        }
    }

    private static Brush CreateDrawableBrush()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 12;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        return brush;
    }

    private static IEnumerable<RenderNode> Descendants(RenderNode node)
    {
        foreach (RenderNode child in node.ChildNodes.ToArray())
        {
            yield return child;
            foreach (RenderNode descendant in Descendants(child))
                yield return descendant;
        }
    }

    [Test]
    public void APreviewThatCannotSpareTheCacheCopy_StillRendersItsFrame()
    {
        using var node = new IntermediateNode(s_bounds);
        node.Cache.RecordStableRequests();
        // Enough for the frame itself, never enough for the extra copy the cache would take.
        var factory = new BudgetedTargetFactory(budget: 2);
        using RenderNodeRenderer renderer = CreateRenderer(node, factory, RenderIntent.Preview);

        Assert.That(() => renderer.Rasterize().Dispose(), Throws.Nothing,
            "A copy that exists only to warm a cache must not fail a frame whose pixels are fine.");
        Assert.That(factory.Declined, Is.GreaterThan(0),
            "The fixture must actually deny the cache its copy.");
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        IRenderTargetFactory factory,
        RenderIntent intent)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = intent,
                    TargetDomain = s_bounds,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = factory,
            });

    /// <summary>A node that publishes one opaque source and therefore needs a buffer of its own.</summary>
    private sealed class IntermediateNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                bounds,
                static (session, area) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(area);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.ContributeValues(context.OpaqueSource(description)));
        }
    }

    /// <summary>Hands out one exact size and declines every other, so a specific allocation runs dry.</summary>
    private sealed class SizedTargetFactory(PixelSize allowed) : IRenderTargetFactory
    {
        public int Declined { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            if (allocation.DeviceSize != allowed)
            {
                Declined++;
                return null;
            }

            return new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
        }
    }

    private sealed class BudgetedTargetFactory(int budget) : IRenderTargetFactory
    {
        private int _granted;

        public int Declined { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            if (_granted >= budget)
            {
                Declined++;
                return null;
            }

            _granted++;
            return new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
