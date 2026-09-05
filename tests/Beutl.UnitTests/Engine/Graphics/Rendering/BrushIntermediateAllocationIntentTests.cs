using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

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
            s_bounds, brush, BlendMode.SrcOver, RenderIntent.Delivery,
            drawableBrushMaterializer: null, scale: UnallocatableScale, maxWorkingScale: 4f);

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
            s_bounds, brush, BlendMode.SrcOver, RenderIntent.Preview,
            drawableBrushMaterializer: null, scale: UnallocatableScale,
            maxWorkingScale: float.PositiveInfinity);

        SKShader? shader = null;
        Assert.That(() => shader = constructor.CreateShader(), Throws.Nothing);
        Assert.That(shader, Is.Null);
    }

    [Test]
    public void DrawableBrush_PreviewDegradesEvenWithoutAWorkingScaleCeiling()
    {
        using DrawableBrush.Resource brush = CreateDrawableBrush();
        bool materialized = false;
        var constructor = new BrushConstructor(
            s_bounds,
            brush,
            BlendMode.SrcOver,
            RenderIntent.Preview,
            scale: UnallocatableScale,
            maxWorkingScale: float.PositiveInfinity,
            drawableBrushMaterializer: (_, contentBounds, _) =>
            {
                materialized = true;
                return new MaterializedDrawableBrush(CreateOpaqueImage(8, 8), contentBounds);
            });

        SKShader? shader = null;
        Assert.That(() => shader = constructor.CreateShader(), Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(materialized, Is.True,
                "The fixture must reach the tile-intermediate allocation to test how it degrades.");
            Assert.That(shader, Is.Null);
        });
    }

    private static SKImage CreateOpaqueImage(int width, int height)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear()))
            ?? throw new InvalidOperationException("The materializer fixture needs a CPU surface.");
        surface.Canvas.Clear(SKColors.White);
        return surface.Snapshot();
    }

    [Test]
    public void UndefinedIntent_IsRejected()
    {
        Assert.That(
            () => new BrushConstructor(
                s_bounds, brush: null, BlendMode.SrcOver, (RenderIntent)12345, drawableBrushMaterializer: null),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("intent"));
    }

    [Test]
    public void Canvas_HandsItsIntentToTheBrushesItPaintsWith()
    {
        using RenderTarget target = RenderTarget.CreateNull(8, 8);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery);
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
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
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
            target, RenderIntent.Delivery, density: 2f, maxWorkingScale: 4f);

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
        => new(root, new RenderNodeRenderRequest
        {
            Intent = intent,
            TargetDomain = s_bounds,
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
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
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class UnallocatableBrushOpaqueNode(Rect bounds) : RenderNode
    {
        private static readonly RenderResourceSlot<Brush.Resource> s_brushSlot = new();

        private readonly Brush.Resource _brush = CreateImageBrush();

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Brush.Resource> brushToken = context.Borrow(_brush);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                bounds,
                static (session, state) => session.UseResource(s_brushSlot, currentBrush =>
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
                resources: [s_brushSlot.Bind(brushToken)],
                slots: [s_brushSlot]);
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
