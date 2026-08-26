using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class BrushMaterializationSmokeTests
{
    [Test]
    public void DrawableBrush_IsMaterializedAtExecution()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        using DrawableBrush.Resource brushResource = brush.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(new Rect(0, 0, 64, 36), brushResource, null);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 64, 36)));
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(GetAlpha(bitmap, 32, 18), Is.GreaterThan(0.9f));
            Assert.That(GetAlpha(bitmap, 0, 0), Is.LessThan(0.1f));
        });
    }

    [Test]
    public void DrawableOpacityMask_IsMaterializedAtExecution()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        using DrawableBrush.Resource brushResource = brush.ToResource(CompositionContext.Default);
        using var root = new OpacityMaskRenderNode(brushResource, new Rect(0, 0, 64, 36), false);
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 64, 36), Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 64, 36)));
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(GetAlpha(bitmap, 32, 18), Is.GreaterThan(0.9f));
            Assert.That(GetAlpha(bitmap, 0, 0), Is.LessThan(0.1f));
        });
    }

    [Test]
    public void DrawableBrushInsidePresenter_IsMaterializedAtExecution()
    {
        var content = new EllipseShape
        {
            Width = { CurrentValue = 20 },
            Height = { CurrentValue = 12 },
            Fill = { CurrentValue = Brushes.White },
        };
        var drawableBrush = new DrawableBrush(content);
        var presenter = new BrushPresenter
        {
            Target = { CurrentValue = drawableBrush },
        };
        using BrushPresenter.Resource presenterResource = presenter.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(new Rect(0, 0, 64, 36), presenterResource, null);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(GetAlpha(bitmap, 32, 18), Is.GreaterThan(0.9f));
            Assert.That(GetAlpha(bitmap, 0, 0), Is.LessThan(0.1f));
        });
    }

    [Test]
    public void BrushPresenterCycle_IsRejectedDeterministically()
    {
        var first = new BrushPresenter();
        var second = new BrushPresenter();
        using BrushPresenter.Resource firstResource = first.ToResource(CompositionContext.Default);
        using BrushPresenter.Resource secondResource = second.ToResource(CompositionContext.Default);
        firstResource.Target = secondResource;
        secondResource.Target = firstResource;

        try
        {
            using var paint = new SKPaint();
            var constructor = new BrushConstructor(
                new Rect(0, 0, 64, 36),
                firstResource,
                BlendMode.SrcOver,
                RenderIntent.Preview,
                drawableBrushMaterializer: null);

            Assert.That(
                () => constructor.ConfigurePaint(paint),
                Throws.InvalidOperationException.With.Message.Contains("BrushPresenter target cycle"));
        }
        finally
        {
            firstResource.Target = null;
            secondResource.Target = null;
        }
    }

    // A directly constructed BrushConstructor inherits no canvas, so without a supplied materializer a
    // DrawableBrush has nothing to rasterize its content with and the fill silently goes transparent.
    [Test]
    public void DirectlyConstructedBrush_PaintsADrawableBrushThroughASuppliedMaterializer()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        using DrawableBrush.Resource brushResource = brush.ToResource(CompositionContext.Default);

        var bounds = new Rect(0, 0, 64, 36);
        DrawableBrushMaterializer materializer =
            (_, _, _) => new MaterializedDrawableBrush(CreateOpaqueImage(20, 12), new Rect(0, 0, 20, 12));

        using var withMaterializer = new SKPaint();
        new BrushConstructor(bounds, brushResource, BlendMode.SrcOver, RenderIntent.Preview, materializer)
            .ConfigurePaint(withMaterializer);
        using var withoutMaterializer = new SKPaint();
        new BrushConstructor(bounds, brushResource, BlendMode.SrcOver, RenderIntent.Preview, null)
            .ConfigurePaint(withoutMaterializer);

        Assert.Multiple(() =>
        {
            Assert.That(withMaterializer.Shader, Is.Not.Null,
                "a supplied materializer must let the public path paint drawable content");
            Assert.That(withoutMaterializer.Shader, Is.Null);
        });
    }

    [Test]
    public void SuppliedMaterializerImage_IsOwnedByTheBrushConstructor()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        using DrawableBrush.Resource brushResource = brush.ToResource(CompositionContext.Default);

        SKImage handedOff = CreateOpaqueImage(20, 12);
        DrawableBrushMaterializer materializer =
            (_, _, _) => new MaterializedDrawableBrush(handedOff, new Rect(0, 0, 20, 12));

        using var paint = new SKPaint();
        new BrushConstructor(
                new Rect(0, 0, 64, 36),
                brushResource,
                BlendMode.SrcOver,
                RenderIntent.Preview,
                materializer)
            .ConfigurePaint(paint);

        Assert.That(handedOff.Handle, Is.EqualTo(IntPtr.Zero),
            "BrushConstructor takes ownership of the materialized image and disposes it before returning");
    }

    [Test]
    public void PublicActivator_PaintsADrawableBrushDisplacementMapThroughASuppliedMaterializer()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 24;
        content.Height.CurrentValue = 24;
        content.Fill.CurrentValue = Brushes.White;
        var effect = new DisplacementMapEffect();
        effect.DisplacementMap.CurrentValue = new DrawableBrush(content);
        effect.ShowDisplacementMap.CurrentValue = true;
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);

        var bounds = new Rect(0, 0, 32, 32);
        using RenderTarget backing = RenderTarget.Create(32, 32)
                                     ?? throw new InvalidOperationException("Could not create the backing target.");
        using var targets = new EffectTargets { new EffectTarget(backing, bounds, EffectiveScale.At(1)) };
        using var builder = new SKImageFilterBuilder();
        using var context = new FilterEffectContext(bounds);
        effect.ApplyTo(context, resource);

        DrawableBrushMaterializer materializer =
            (_, _, _) => new MaterializedDrawableBrush(CreateOpaqueImage(32, 32), bounds);
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: materializer);
        activator.Apply(context);
        activator.Flush(false);

        using Bitmap bitmap = targets[0].RenderTarget!.Snapshot();

        Assert.That(GetAlpha(bitmap, 16, 16), Is.GreaterThan(0.9f),
            "a materializer supplied to the public activator must reach the custom-effect brush constructor");
    }

    // The absent hook is the failure, not the brush: a delivery frame that painted the fill transparent would
    // ship a hole and report success, while a preview is allowed to drop it and keep going.
    [Test]
    public void ADeliveryFillWithoutAMaterializer_FailsInsteadOfPaintingAHole()
    {
        using DrawableBrush.Resource brushResource = CreateDrawableBrushResource();

        using var paint = new SKPaint();
        var constructor = new BrushConstructor(
            new Rect(0, 0, 64, 36),
            brushResource,
            BlendMode.SrcOver,
            RenderIntent.Delivery,
            drawableBrushMaterializer: null);

        Assert.That(
            () => constructor.ConfigurePaint(paint),
            Throws.InvalidOperationException.With.Message.Contains("no runtime materializer"));
    }

    [Test]
    public void APreviewFillWithoutAMaterializer_StillDegradesQuietly()
    {
        using DrawableBrush.Resource brushResource = CreateDrawableBrushResource();

        using var paint = new SKPaint();
        var constructor = new BrushConstructor(
            new Rect(0, 0, 64, 36),
            brushResource,
            BlendMode.SrcOver,
            RenderIntent.Preview,
            drawableBrushMaterializer: null);

        Assert.That(() => constructor.ConfigurePaint(paint), Throws.Nothing);
        Assert.That(paint.Shader, Is.Null);
        Assert.That(paint.Color, Is.EqualTo(SKColors.Transparent));
    }

    [Test]
    public void APublicCanvas_TakesAMaterializerAndHandsItToItsBrushes()
    {
        DrawableBrushMaterializer materializer =
            (_, _, _) => new MaterializedDrawableBrush(CreateOpaqueImage(20, 12), new Rect(0, 0, 20, 12));
        using RenderTarget target = RenderTarget.Create(64, 36)
                                    ?? throw new InvalidOperationException("Could not create the canvas target.");
        using var canvas = new ImmediateCanvas(
            target,
            RenderIntent.Delivery,
            drawableBrushMaterializer: materializer);

        Assert.That(canvas.DrawableBrushMaterializer, Is.SameAs(materializer));
        Assert.That(
            canvas.CreateBrushConstructor(new Rect(0, 0, 64, 36), Brushes.Resource.White, BlendMode.SrcOver)
                .Intent,
            Is.EqualTo(RenderIntent.Delivery));
    }

    private static DrawableBrush.Resource CreateDrawableBrushResource()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        return new DrawableBrush(content).ToResource(CompositionContext.Default);
    }

    private static SKImage CreateOpaqueImage(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info)
                                  ?? throw new InvalidOperationException("Could not create the source surface.");
        surface.Canvas.Clear(SKColors.White);
        return surface.Snapshot();
    }

    private static float GetAlpha(Bitmap bitmap, int x, int y)
        => (float)bitmap.GetRow<Half>(y)[(x * 4) + 3];
}
