using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Brush-owned intermediates must decide degrade-vs-fail from the explicit <see cref="RenderIntent"/>,
/// not from the working-scale ceiling that happens to accompany it.
/// </summary>
[TestFixture]
public sealed class BrushIntermediateAllocationIntentTests
{
    // Larger than any GPU or raster allocation can satisfy, so RenderTarget.Create returns null fast.
    private const float UnallocatableScale = 2_000_000f;

    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void TileBrush_DeliveryFailsEvenWithAFiniteWorkingScaleCeiling()
    {
        using ImageBrush.Resource brush = CreateImageBrush();
        var constructor = new BrushConstructor(
            s_bounds, brush, BlendMode.SrcOver, UnallocatableScale,
            maxWorkingScale: 4f, intent: RenderIntent.Delivery);

        Assert.That(
            () => constructor.CreateShader(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartWith("Tile-brush intermediate allocation failed"));
    }

    [Test]
    public void TileBrush_PreviewDegradesEvenWithoutAWorkingScaleCeiling()
    {
        using ImageBrush.Resource brush = CreateImageBrush();
        var constructor = new BrushConstructor(
            s_bounds, brush, BlendMode.SrcOver, UnallocatableScale,
            maxWorkingScale: float.PositiveInfinity, intent: RenderIntent.Preview);

        SKShader? shader = null;
        Assert.That(() => shader = constructor.CreateShader(), Throws.Nothing);
        Assert.That(shader, Is.Null);
    }

    [Test]
    public void DrawableBrush_DeliveryFailsEvenWithAFiniteWorkingScaleCeiling()
    {
        using DrawableBrush.Resource brush = CreateDrawableBrush();
        using SKShader tile = SKShader.CreateColor(SKColors.White);
        var constructor = new BrushConstructor(
            s_bounds,
            new LoweredBrush(null, brush, new BrushTileContent(tile, s_bounds, EffectiveScale.At(1f))),
            BlendMode.SrcOver,
            UnallocatableScale,
            maxWorkingScale: 4f,
            intent: RenderIntent.Delivery);

        Assert.That(
            () => constructor.CreateShader(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartWith("Drawable-brush intermediate allocation failed"));
    }

    [Test]
    public void DrawableBrush_PreviewDegradesEvenWithoutAWorkingScaleCeiling()
    {
        using DrawableBrush.Resource brush = CreateDrawableBrush();
        using SKShader tile = SKShader.CreateColor(SKColors.White);
        var constructor = new BrushConstructor(
            s_bounds,
            new LoweredBrush(null, brush, new BrushTileContent(tile, s_bounds, EffectiveScale.At(1f))),
            BlendMode.SrcOver,
            UnallocatableScale,
            maxWorkingScale: float.PositiveInfinity,
            intent: RenderIntent.Preview);

        SKShader? shader = null;
        Assert.That(() => shader = constructor.CreateShader(), Throws.Nothing);
        Assert.That(shader, Is.Null);
    }

    [Test]
    public void UndefinedIntent_IsRejected()
    {
        Assert.That(
            () => new BrushConstructor(s_bounds, brush: null, BlendMode.SrcOver, intent: (RenderIntent)12345),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("intent"));
    }

    [Test]
    public void Canvas_HandsItsIntentToTheBrushesItPaintsWith()
    {
        using RenderTarget target = RenderTarget.CreateNull(8, 8);
        using var canvas = new ImmediateCanvas(target, intent: RenderIntent.Delivery);
        using ImageBrush.Resource brush = CreateImageBrush();

        Assert.That(canvas.Intent, Is.EqualTo(RenderIntent.Delivery));
        Assert.That(
            () => canvas.DrawRectangle(new Rect(0, 0, UnallocatableScale, UnallocatableScale), brush, pen: null),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartWith("Tile-brush intermediate allocation failed"),
            "A delivery canvas must propagate its intent into the brush intermediates it allocates.");
    }

    [Test]
    public void PreviewCanvas_KeepsDrawingWhenABrushIntermediateCannotBeAllocated()
    {
        using RenderTarget target = RenderTarget.CreateNull(8, 8);
        using var canvas = new ImmediateCanvas(target);
        using ImageBrush.Resource brush = CreateImageBrush();

        Assert.That(canvas.Intent, Is.EqualTo(RenderIntent.Preview));
        Assert.That(
            () => canvas.DrawRectangle(new Rect(0, 0, UnallocatableScale, UnallocatableScale), brush, pen: null),
            Throws.Nothing);
    }

    [Test]
    public void CanvasBrushConstructorHelper_CarriesDensityCeilingAndIntent()
    {
        using RenderTarget target = RenderTarget.CreateNull(8, 8);
        using var canvas = new ImmediateCanvas(
            target, density: 2f, maxWorkingScale: 4f, intent: RenderIntent.Delivery);

        BrushConstructor constructor = canvas.CreateBrushConstructor(
            s_bounds, Brushes.Resource.White, BlendMode.SrcOver);

        Assert.Multiple(() =>
        {
            Assert.That(constructor.Scale, Is.EqualTo(canvas.Density));
            Assert.That(constructor.MaxWorkingScale, Is.EqualTo(canvas.MaxWorkingScale));
            Assert.That(constructor.Intent, Is.EqualTo(RenderIntent.Delivery));
        });
    }

    // The executor-managed canvases are where most intermediate allocation happens; a delivery request
    // that reached them as Preview degraded silently.
    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void TheOpaqueCallbackCanvasCarriesTheRequestIntent(RenderIntent intent)
    {
        RenderIntent? observed = null;
        using var node = new IntentProbeOpaqueNode(s_bounds, canvas => observed = canvas.Intent);
        using RenderNodeRenderer renderer = CreateRenderer(node, intent);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.That(observed, Is.EqualTo(intent));
    }

    [Test]
    public void ADeliveryRequestFailsWhenAnOpaqueCallbackCannotAllocateItsBrushIntermediate()
    {
        using var node = new UnallocatableBrushOpaqueNode(s_bounds);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderIntent.Delivery);

        Assert.That(
            () => renderer.Rasterize(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartWith("Tile-brush intermediate allocation failed"));
    }

    [Test]
    public void APreviewRequestAbsorbsTheSameOpaqueCallbackAllocationFailure()
    {
        using var node = new UnallocatableBrushOpaqueNode(s_bounds);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderIntent.Preview);

        Assert.That(() => renderer.Rasterize().Dispose(), Throws.Nothing);
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void TheTargetCommandCanvasCarriesTheRequestIntent(RenderIntent intent)
    {
        RenderIntent? observed = null;
        using var node = new TargetCommandProbeNode(s_bounds, canvas => observed = canvas.Intent);
        using RenderNodeRenderer renderer = CreateRenderer(node, intent);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.That(observed, Is.EqualTo(intent));
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root, RenderIntent intent)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = intent,
                    TargetDomain = s_bounds,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

    private static ImageBrush.Resource CreateImageBrush()
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
        return brush.ToResource(CompositionContext.Default);
    }

    private static DrawableBrush.Resource CreateDrawableBrush()
    {
        var content = new RectShape();
        content.Width.CurrentValue = 8;
        content.Height.CurrentValue = 8;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        brush.TileMode.CurrentValue = TileMode.None;
        brush.DestinationRect.CurrentValue = RelativeRect.Fill;
        return brush.ToResource(CompositionContext.Default);
    }

    private sealed class IntentProbeOpaqueNode(Rect bounds, Action<ImmediateCanvas> probe) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(probe);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "brush-intent-probe-source");
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class UnallocatableBrushOpaqueNode(Rect bounds) : RenderNode
    {
        private readonly Brush.Resource _brush = CreateImageBrush();

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Brush.Resource> brushToken = context.Borrow(
                _brush,
                _brush.GetOriginal().Id,
                _brush.Version);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                bounds,
                static (session, state) => session.UseDeclaredResource<Brush.Resource>("brush", currentBrush =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(state);
                    output.Canvas.Use(canvas => canvas.DrawRectangle(
                        new Rect(0, 0, UnallocatableScale, UnallocatableScale),
                        currentBrush,
                        pen: null));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "brush-intent-unallocatable-source",
                resources: [brushToken.Bind("brush")]);
            context.Publish(context.OpaqueSource(description));
        }

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            if (disposing)
                _brush.Dispose();
        }
    }

    private sealed class TargetCommandProbeNode(Rect bounds, Action<ImmediateCanvas> probe) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            TargetCommandDescription command = TargetCommandDescription.CreateRequestLocal(
                session => session.Canvas.Use(probe),
                TargetRegion.Region(bounds),
                bounds,
                RenderHitTestContract.None);
            context.Publish(context.TargetCommand([], command));
        }
    }
}
