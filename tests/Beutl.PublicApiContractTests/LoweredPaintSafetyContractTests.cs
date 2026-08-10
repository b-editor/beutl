using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Proves that the safety story <see cref="RenderNodeContext.PaintedSource{TState}"/> promises holds on the
/// path it actually takes: the callback receives only replay-safe drawing operations, a resolved paint cannot
/// outlive its lease, and a declared supply density is honoured instead of silently replaced by the
/// destination's.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class LoweredPaintSafetyContractTests
{
    private static readonly Rect s_rect = new(0, 0, 64, 36);
    private static Bitmap? s_undeclaredBitmap;

    [Test]
    public void DrawCallbackCanvas_ExposesOnlyReplaySafeOperations()
    {
        Type canvas = typeof(PaintedRenderCanvas);
        const BindingFlags publicSurfaceFlags = BindingFlags.Public
                                                | BindingFlags.Instance
                                                | BindingFlags.Static
                                                | BindingFlags.DeclaredOnly;
        MethodInfo[] publicMethods =
        [
            .. canvas.GetMethods(publicSurfaceFlags),
        ];
        PropertyInfo[] publicProperties =
        [
            .. canvas.GetProperties(publicSurfaceFlags),
        ];
        MemberInfo[] unsupportedMembers =
        [
            .. canvas
                .GetMembers(publicSurfaceFlags)
                .Where(static member => member is not MethodInfo and not PropertyInfo),
        ];
        Type[] publicNestedTypes = canvas.GetNestedTypes(BindingFlags.Public);
        (string Name, Type ReturnType, Type[] Parameters, bool IsSpecialName)[] expectedMethods =
        [
            (nameof(PaintedRenderCanvas.DrawBitmap), typeof(void),
                [typeof(Bitmap), typeof(LoweredBrush), typeof(LoweredPen)], false),
            (nameof(PaintedRenderCanvas.DrawBitmapScaled), typeof(void),
                [typeof(Bitmap), typeof(Rect), typeof(LoweredBrush)], false),
            (nameof(PaintedRenderCanvas.DrawImageSource), typeof(void),
                [typeof(ImageSource.Resource), typeof(LoweredBrush), typeof(LoweredPen)], false),
            (nameof(PaintedRenderCanvas.DrawVideoSource), typeof(void),
                [typeof(VideoSource.Resource), typeof(int), typeof(LoweredBrush), typeof(LoweredPen)], false),
            (nameof(PaintedRenderCanvas.DrawEllipse), typeof(void),
                [typeof(Rect), typeof(LoweredBrush), typeof(LoweredPen)], false),
            (nameof(PaintedRenderCanvas.DrawRectangle), typeof(void),
                [typeof(Rect), typeof(LoweredBrush), typeof(LoweredPen)], false),
            (nameof(PaintedRenderCanvas.DrawText), typeof(void),
                [typeof(FormattedText), typeof(LoweredBrush), typeof(LoweredPen)], false),
            (nameof(PaintedRenderCanvas.DrawGeometry), typeof(void),
                [typeof(Geometry.Resource), typeof(LoweredBrush), typeof(LoweredPen)], false),
            ($"get_{nameof(PaintedRenderCanvas.Density)}", typeof(float), [], true),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(PaintedRenderSession).GetProperty(nameof(PaintedRenderSession.Canvas))!.PropertyType,
                Is.EqualTo(canvas));
            Assert.That(publicMethods, Has.Length.EqualTo(expectedMethods.Length));
            Assert.That(publicProperties, Has.Length.EqualTo(1));
            Assert.That(publicProperties[0].Name, Is.EqualTo(nameof(PaintedRenderCanvas.Density)));
            Assert.That(publicProperties[0].PropertyType, Is.EqualTo(typeof(float)));
            Assert.That(publicProperties[0].CanRead, Is.True);
            Assert.That(publicProperties[0].CanWrite, Is.False);
            Assert.That(publicProperties[0].GetIndexParameters(), Is.Empty);
            Assert.That(unsupportedMembers, Is.Empty);
            Assert.That(publicNestedTypes, Is.Empty);
        });

        foreach ((string name, Type returnType, Type[] parameters, bool isSpecialName) in expectedMethods)
        {
            MethodInfo[] candidates = [.. publicMethods.Where(method => method.Name == name)];
            Assert.That(candidates, Has.Length.EqualTo(1), name);
            Assert.Multiple(() =>
            {
                Assert.That(candidates[0].ReturnType, Is.EqualTo(returnType), name);
                Assert.That(candidates[0].IsGenericMethod, Is.False, name);
                Assert.That(candidates[0].IsStatic, Is.False, name);
                Assert.That(candidates[0].IsSpecialName, Is.EqualTo(isSpecialName), name);
                Assert.That(
                    candidates[0].GetParameters().Select(static parameter => parameter.ParameterType),
                    Is.EqualTo(parameters),
                    name);
            });
        }
    }

    [Test]
    public void DrawCallbackCanvas_MaterializedAndDirectPathsAreByteIdentical()
    {
        using var directNode = new SafePaintNode(s_rect, RenderScaleContract.Vector);
        using var materializedNode = new SafePaintNode(
            s_rect,
            RenderScaleContract.Custom(static _ => 1f, "materialized-paint"));
        using RenderNodeRenderer directRenderer = CreateRenderer(directNode);
        using RenderNodeRenderer materializedRenderer = CreateRenderer(materializedNode);
        using RenderNodeRasterization direct = directRenderer.Rasterize();
        using RenderNodeRasterization materialized = materializedRenderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(direct.IsEmpty, Is.False);
            Assert.That(materialized.IsEmpty, Is.False);
            Assert.That(materialized.Bounds, Is.EqualTo(direct.Bounds));
            Assert.That(
                materialized.Bitmap!.GetPixelSpan().SequenceEqual(direct.Bitmap!.GetPixelSpan()),
                Is.True,
                "The draw-only surface must render identically with and without direct replay.");
        });
    }

    [Test]
    public void RequestLocalDrawCallback_MaterializedAndDirectPathsAreByteIdentical()
    {
        using var directNode = new RequestLocalSafePaintNode(s_rect, RenderScaleContract.Vector);
        using var materializedNode = new RequestLocalSafePaintNode(
            s_rect,
            RenderScaleContract.Custom(static _ => 1f, "request-local-materialized-paint"));
        using RenderNodeRenderer directRenderer = CreateRenderer(directNode);
        using RenderNodeRenderer materializedRenderer = CreateRenderer(materializedNode);
        using RenderNodeRasterization direct = directRenderer.Rasterize();
        using RenderNodeRasterization materialized = materializedRenderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(direct.IsEmpty, Is.False);
            Assert.That(materialized.IsEmpty, Is.False);
            Assert.That(materialized.Bounds, Is.EqualTo(direct.Bounds));
            Assert.That(
                materialized.Bitmap!.GetPixelSpan().SequenceEqual(direct.Bitmap!.GetPixelSpan()),
                Is.True,
                "Request-local identity must change cache reuse only, not direct-replay pixels.");
        });
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

    private sealed class SafePaintNode(Rect rect, RenderScaleContract scale) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSource(
                state: rect,
                draw: static (session, state) => session.Canvas.DrawRectangle(
                    state,
                    session.Fill,
                    session.Pen),
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: scale,
                structuralKey: "safe-paint"));
        }
    }

    private sealed class RequestLocalSafePaintNode(Rect rect, RenderScaleContract scale) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.PaintedSourceRequestLocal(
                draw: session => session.Canvas.DrawRectangle(rect, session.Fill, session.Pen),
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: rect,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: scale,
                structuralKey: "request-local-safe-paint"));
        }
    }

    private sealed class BitmapPaintNode(Rect rect, Bitmap bitmap, bool scaled) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            s_undeclaredBitmap = bitmap;
            context.Publish(context.PaintedSource(
                state: (Rect: rect, Scaled: scaled),
                draw: static (session, state) =>
                {
                    if (state.Scaled)
                        session.Canvas.DrawBitmapScaled(s_undeclaredBitmap!, state.Rect, session.Fill);
                    else
                        session.Canvas.DrawBitmap(s_undeclaredBitmap!, session.Fill, session.Pen);
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

    private sealed class RetainingPaintNode(Rect rect, Brush.Resource fill, PaintRetainer retainer) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<PaintRetainer> retainerResource = context.Borrow(retainer);
            context.Publish(context.PaintedSource(
                primary: retainerResource,
                state: rect,
                draw: static (session, currentRetainer, state) =>
                {
                    if (currentRetainer.ReplayRetained)
                        currentRetainer.DrawRetained(session.Canvas, state);
                    else
                        currentRetainer.Retain(session.Fill);

                    session.Canvas.DrawRectangle(state, session.Fill, session.Pen);
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

        public void DrawRetained(PaintedRenderCanvas canvas, Rect rect)
            => canvas.DrawRectangle(rect, _fill, LoweredPen.Empty);

        public void DrawRetained(ImmediateCanvas canvas, Rect rect)
            => canvas.DrawRectangle(rect, _fill, LoweredPen.Empty);
    }

    private sealed class DeclaredDensityPaintNode(Rect rect, DensityProbe probe) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<DensityProbe> probeResource = context.Borrow(probe);
            context.Publish(context.PaintedSource(
                primary: probeResource,
                state: rect,
                draw: static (session, currentProbe, state) =>
                {
                    currentProbe.Density = session.Canvas.Density;
                    session.Canvas.DrawRectangle(state, session.Fill, session.Pen);
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
            RenderResource<DensityProbe> probeResource = context.Borrow(probe);
            context.Publish(context.PaintedSource(
                primary: probeResource,
                state: rect,
                draw: static (session, currentProbe, state) =>
                {
                    currentProbe.Density = session.Canvas.Density;
                    session.Canvas.DrawRectangle(state, session.Fill, session.Pen);
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
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation)
            => RenderScaleUtilities.MaxBufferDimension;

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
