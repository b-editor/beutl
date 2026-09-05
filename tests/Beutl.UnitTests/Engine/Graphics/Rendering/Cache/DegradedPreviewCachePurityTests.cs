using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class DegradedPreviewCachePurityTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 24);

    // Read once here rather than inside the callback: a Colors member is a get-only property whose getter
    // this compilation cannot see, so a callback naming it is not shown to answer the same way twice.
    private static readonly Color s_white = Colors.White;

    [Test]
    public void ARequestOnADeliveryCanvas_FailsRatherThanDroppingContent()
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

        var factory = new SizedTargetFactory(PixelRect.FromRect(s_bounds, 1).Size);
        using RenderNodeRenderer renderer = CreateRenderer(root, factory, RenderIntent.Preview);
        using RenderTarget target = new CpuRenderTarget(
            PixelRect.FromRect(s_bounds, 1).Width,
            PixelRect.FromRect(s_bounds, 1).Height);
        using var delivery = new ImmediateCanvas(
            target,
            RenderIntent.Delivery,
            density: 1,
            maxWorkingScale: 1,
            logicalSize: s_bounds.Size);

        Assert.That(
            () => renderer.Render(delivery),
            Throws.InvalidOperationException,
            "A preview request must not be allowed to drop content into a delivery surface.");
    }

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

    [Test]
    public void APreviewWhoseTileBrushDroppedItsIntermediate_CommitsNoBackdropSnapshot()
    {
        // The frame's own target is the only size this factory hands out, and the tile intermediate is
        // sized from the shape, which is smaller.
        var factory = new SizedTargetFactory(PixelRect.FromRect(s_bounds, 1).Size);
        BackdropSinkProbeNode probe = RenderTileBrushFrame(factory);

        Assert.Multiple(() =>
        {
            Assert.That(factory.Declined, Is.GreaterThan(0),
                "The fixture must actually make the tile brush run out of targets.");
            Assert.That(probe.Commits, Is.Zero,
                "A frame whose tile fill degraded to transparent has nothing fit to outlive it.");
        });
    }

    [Test]
    public void APreviewWhoseTileBrushKeptItsIntermediate_CommitsItsBackdropSnapshot()
    {
        var factory = new BudgetedTargetFactory(budget: 16);
        BackdropSinkProbeNode probe = RenderTileBrushFrame(factory);

        Assert.Multiple(() =>
        {
            Assert.That(factory.Declined, Is.Zero, "precondition: nothing ran out of targets");
            Assert.That(probe.Commits, Is.EqualTo(1),
                "Without this the guard above would hold for a frame that never committed anything.");
        });
    }

    [Test]
    public void BackdropPublicationFailure_DisposesTheCaptureAndReleasesEveryTarget()
    {
        var primary = new InvalidOperationException("backdrop-publication-primary");
        using var root = new ContainerRenderNode();
        root.AddChild(new IntermediateNode(s_bounds));
        var probe = new ThrowingBackdropSinkProbeNode(primary);
        root.AddChild(probe);
        var factory = new BudgetedTargetFactory(budget: 16);
        using RenderNodeRenderer renderer = CreateRenderer(root, factory, RenderIntent.Preview);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(probe.ReceivedBitmap, Is.Not.Null);
            Assert.That(probe.ReceivedBitmap!.IsDisposed, Is.True);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    private static BackdropSinkProbeNode RenderTileBrushFrame(IRenderTargetFactory factory)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = 16;
        shape.Height.CurrentValue = 12;
        shape.Fill.CurrentValue = CreateTileBrush();
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
        using RenderNodeRenderer renderer = CreateRenderer(root, factory, RenderIntent.Preview);

        renderer.Rasterize().Dispose();
        return probe;
    }

    private static Brush CreateTileBrush()
    {
        using var bitmap = new Bitmap(4, 4);
        using var stream = new MemoryStream();
        bitmap.Save(stream, EncodedImageFormat.Png);

        var source = new ImageSource();
        source.ReadFrom(UriHelper.CreateBase64DataUri("image/png", stream.ToArray()));
        var brush = new ImageBrush(source);
        brush.Stretch.CurrentValue = Stretch.Fill;
        brush.TileMode.CurrentValue = TileMode.None;
        brush.DestinationRect.CurrentValue = RelativeRect.Fill;
        return brush;
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

    private sealed class ThrowingBackdropSinkProbeNode(InvalidOperationException primaryFailure)
        : SnapshotBackdropRenderNode, IBuiltInBackdropCaptureSink
    {
        public Bitmap? ReceivedBitmap { get; private set; }

        bool IBuiltInBackdropCaptureSink.TryCommitBackdropCapture(Bitmap bitmap, float density)
        {
            ReceivedBitmap = bitmap;
            throw primaryFailure;
        }

        void IBuiltInBackdropCaptureSink.CommitBackdropCapture(Bitmap bitmap, float density)
            => throw new AssertionException("The renderer must use the non-throwing publication contract.");
    }

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
                    output.Canvas.Use(static canvas => canvas.Clear(s_white));
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
        => new(node, new RenderNodeRenderRequest
        {
            Intent = intent,
            TargetDomain = s_bounds,
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = RenderCacheOptions.Enabled,
            Purpose = RenderRequestPurpose.Frame,
        }, factory);

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
                    output.Canvas.Use(static canvas => canvas.Clear(s_white));
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
