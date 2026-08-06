using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Proves that the safety story <see cref="RenderNodeContext.PaintedSource{TState}"/> promises holds on the
/// path it actually takes: the canvas the draw callback receives is guarded whether the source materializes
/// into its own target or replays straight onto an existing one, a resolved paint cannot outlive its lease,
/// and a declared supply density is honoured instead of silently replaced by the destination's.
/// </summary>
[TestFixture]
public sealed class LoweredPaintSafetyContractTests
{
    private static readonly Rect s_rect = new(0, 0, 64, 36);

    [Test]
    public void DrawCallbackCanvas_RejectsSaveLayerBackedState()
    {
        using var node = new HiddenLayerPaintNode(s_rect);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        Exception? failure = Assert.Catch(() => renderer.Rasterize().Dispose());

        TestContext.Out.WriteLine(failure?.ToString());
        Assert.That(
            failure,
            Is.TypeOf<InvalidOperationException>().With.Message.Contains("guarded callback canvas"),
            "a painted source's canvas is executor-managed on every path it can run on");
    }

    [Test]
    public void DrawCallbackCanvas_IsStillGuardedInsideATargetLayerScope()
    {
        using var node = new HiddenLayerPaintNode(s_rect, isolate: true);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        Exception? failure = Assert.Catch(() => renderer.Rasterize().Dispose());

        TestContext.Out.WriteLine(failure?.ToString());
        Assert.That(
            failure,
            Is.TypeOf<InvalidOperationException>().With.Message.Contains("guarded callback canvas"),
            "replaying a scope's stream does not relax the guard for a painted source inside it");
    }

    [Test]
    public void DrawCallbackCanvas_RejectsAnUndeclaredBrushResource()
    {
        var undeclared = new SolidColorBrush { Color = { CurrentValue = Colors.Red } };
        using Brush.Resource undeclaredResource = (Brush.Resource)undeclared.ToResource(CompositionContext.Default);
        using var node = new UndeclaredBrushPaintNode(s_rect, undeclaredResource);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        Exception? failure = Assert.Catch(() => renderer.Rasterize().Dispose());

        TestContext.Out.WriteLine(failure?.ToString());
        Assert.That(
            failure,
            Is.TypeOf<InvalidOperationException>().With.Message.Contains("not authorized"),
            "an undeclared brush must be rejected on the direct-replay canvas too");
    }

    [Test]
    public void RetainedLoweredPaint_IsRejectedOnALaterFrame()
    {
        using var content = new DrawableContent(Colors.White);
        var retainer = new PaintRetainer();
        using var node = new RetainingPaintNode(s_rect, content.BrushResource, retainer);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        renderer.Rasterize().Dispose();
        retainer.ReplayRetained = true;
        Exception? failure = Assert.Catch(() => renderer.Rasterize().Dispose());

        TestContext.Out.WriteLine(failure?.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(retainer.Retained, Is.True, "the first frame must have handed out a resolved paint");
            Assert.That(
                failure,
                Is.TypeOf<InvalidOperationException>().With.Message.Contains("lease"),
                "the lowered content was leased for one callback and cannot be drawn with afterwards");
        });
    }

    [Test]
    public void RetainedLoweredPaint_IsRejectedOnAnAuthorOwnedCanvas()
    {
        using var content = new DrawableContent(Colors.White);
        var retainer = new PaintRetainer();
        using var node = new RetainingPaintNode(s_rect, content.BrushResource, retainer);
        using (RenderNodeRenderer renderer = CreateRenderer(node))
        {
            renderer.Rasterize().Dispose();
        }

        Assert.That(retainer.Retained, Is.True);

        using RenderTarget target = new CpuRenderTarget(64, 36);
        using var canvas = new ImmediateCanvas(target);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => retainer.DrawRetained(canvas, s_rect));

        TestContext.Out.WriteLine(failure!.Message);
        Assert.That(
            failure.Message,
            Does.Contain("lease"),
            "the guard must not depend on which canvas the retained paint is handed to");
    }

    [Test]
    public void DrawBitmapUnderALoweredPaint_RejectsAnUndeclaredBitmap()
    {
        using var bitmap = new Bitmap(8, 8);
        using var node = new BitmapPaintNode(s_rect, bitmap, scaled: false);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        Exception? failure = Assert.Catch(() => renderer.Rasterize().Dispose());

        TestContext.Out.WriteLine(failure?.ToString());
        Assert.That(
            failure,
            Is.TypeOf<InvalidOperationException>().With.Message.Contains("not authorized"),
            "the lowered overload must verify the bitmap exactly like its Brush.Resource sibling");
    }

    [Test]
    public void DrawBitmapScaledUnderALoweredBrush_RejectsAnUndeclaredBitmap()
    {
        using var bitmap = new Bitmap(8, 8);
        using var node = new BitmapPaintNode(s_rect, bitmap, scaled: true);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        Exception? failure = Assert.Catch(() => renderer.Rasterize().Dispose());

        TestContext.Out.WriteLine(failure?.ToString());
        Assert.That(
            failure,
            Is.TypeOf<InvalidOperationException>().With.Message.Contains("not authorized"),
            "the lowered overload must verify the bitmap exactly like its Brush.Resource sibling");
    }

    [Test]
    public void ConcreteDeclaredDensity_ReachesTheDrawCallback()
    {
        var probe = new DensityProbe();
        using var node = new DeclaredDensityPaintNode(s_rect, probe);
        using RenderNodeRenderer renderer = CreateRenderer(node, outputScale: 2f);

        renderer.Rasterize().Dispose();

        TestContext.Out.WriteLine($"callback density: {probe.Density}");
        Assert.That(
            probe.Density,
            Is.EqualTo(1f),
            "a source that declares a concrete supply density must render at it, not at the destination's");
    }

    [Test]
    public void VectorDensity_StillFollowsTheDestination()
    {
        var probe = new DensityProbe();
        using var node = new VectorDensityPaintNode(s_rect, probe);
        using RenderNodeRenderer renderer = CreateRenderer(node, outputScale: 2f);

        renderer.Rasterize().Dispose();

        TestContext.Out.WriteLine($"callback density: {probe.Density}");
        Assert.That(
            probe.Density,
            Is.EqualTo(2f),
            "a vector source declares no density, so it keeps rendering at the destination's");
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node, float outputScale = 1f)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_rect,
                    OutputScale = outputScale,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private sealed class HiddenLayerPaintNode(Rect rect, bool isolate = false) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle painted = context.PaintedSource(
                state: rect,
                draw: static (session, state) =>
                {
                    using (session.Canvas.PushOpacity(0.5f))
                        session.Canvas.DrawRectangle(state, session.Fill, session.Pen);
                },
                fill: null,
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "hidden-layer-paint");
            context.Publish(isolate
                ? context.TargetLayerScope([painted], TargetRegion.Full)
                : painted);
        }
    }

    private sealed class UndeclaredBrushPaintNode(Rect rect, Brush.Resource undeclared) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                state: (rect, new UndeclaredBrushHolder(undeclared)),
                draw: static (session, state) =>
                    session.Canvas.DrawRectangle(state.Item1, state.Item2.Brush, null),
                fill: null,
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "undeclared-brush-paint"));
        }
    }

    private sealed class UndeclaredBrushHolder(Brush.Resource brush)
    {
        public Brush.Resource Brush { get; } = brush;
    }

    private sealed class BitmapPaintNode(Rect rect, Bitmap bitmap, bool scaled) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                state: (rect, new BitmapHolder(bitmap), scaled),
                draw: static (session, state) =>
                {
                    if (state.scaled)
                        session.Canvas.DrawBitmapScaled(state.Item2.Bitmap, state.Item1, session.Fill);
                    else
                        session.Canvas.DrawBitmap(state.Item2.Bitmap, session.Fill, session.Pen);
                },
                fill: null,
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "bitmap-paint"));
        }
    }

    private sealed class BitmapHolder(Bitmap bitmap)
    {
        public Bitmap Bitmap { get; } = bitmap;
    }

    private sealed class RetainingPaintNode(Rect rect, Brush.Resource fill, PaintRetainer retainer) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                state: (rect, retainer),
                draw: static (session, state) =>
                {
                    if (state.Item2.ReplayRetained)
                        state.Item2.DrawRetained(session.Canvas, state.Item1);
                    else
                        state.Item2.Retain(session.Fill);

                    session.Canvas.DrawRectangle(state.Item1, session.Fill, session.Pen);
                },
                fill: fill.Capture(),
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "retaining-paint"));
        }
    }

    /// <summary>Retains a resolved paint the way a mutable authored state can, and replays it later.</summary>
    private sealed class PaintRetainer
    {
        private LoweredBrush _fill;

        public bool Retained { get; private set; }

        public bool ReplayRetained { get; set; }

        public void Retain(LoweredBrush fill)
        {
            _fill = fill;
            Retained = true;
        }

        public void DrawRetained(ImmediateCanvas canvas, Rect rect)
            => canvas.DrawRectangle(rect, _fill, LoweredPen.Empty);
    }

    private sealed class DeclaredDensityPaintNode(Rect rect, DensityProbe probe) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                state: (rect, probe),
                draw: static (session, state) =>
                {
                    state.Item2.Density = session.Canvas.Density;
                    session.Canvas.DrawRectangle(state.Item1, session.Fill, session.Pen);
                },
                fill: null,
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Custom(static _ => 1f, "declared-density"),
                structuralKey: "declared-density-paint"));
        }
    }

    private sealed class VectorDensityPaintNode(Rect rect, DensityProbe probe) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                state: (rect, probe),
                draw: static (session, state) =>
                {
                    state.Item2.Density = session.Canvas.Density;
                    session.Canvas.DrawRectangle(state.Item1, session.Fill, session.Pen);
                },
                fill: null,
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "vector-density-paint"));
        }
    }

    private sealed class DensityProbe
    {
        public float Density { get; set; }
    }

    private sealed class DrawableContent : IDisposable
    {
        private readonly RectShape _content;

        public DrawableContent(Color color)
        {
            _content = new RectShape
            {
                Width = { CurrentValue = (float)s_rect.Width },
                Height = { CurrentValue = (float)s_rect.Height },
                Fill = { CurrentValue = new SolidColorBrush { Color = { CurrentValue = color } } },
            };
            Brush = new DrawableBrush(_content) { Stretch = { CurrentValue = Stretch.Fill } };
            BrushResource = Brush.ToResource(CompositionContext.Default);
        }

        public DrawableBrush Brush { get; }

        public DrawableBrush.Resource BrushResource { get; }

        public void Dispose() => BrushResource.Dispose();
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
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
