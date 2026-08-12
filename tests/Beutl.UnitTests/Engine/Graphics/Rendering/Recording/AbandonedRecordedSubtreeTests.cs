using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

// These bail-outs learn there is nothing to draw only after recording a child subtree, so they
// abandon a subtree that may already have published target-effect fragments of its own.
[TestFixture]
public sealed class AbandonedRecordedSubtreeTests
{
    private static readonly Rect s_ownerRect = new(0, 0, 32, 24);

    public enum DegenerateBrushContent
    {
        NoDrawable,
        DisabledDrawable,
        ZeroAreaDrawable,
    }

    [Test]
    public void ParticleRenderNode_WithParticlesScaledToZero_RendersNothingWithoutFailing()
    {
        var particle = new RectShape();
        particle.Width.CurrentValue = 20;
        particle.Height.CurrentValue = 12;
        particle.Fill.CurrentValue = Brushes.White;

        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = particle;
        emitter.MaxParticles.CurrentValue = 1;
        emitter.Speed.CurrentValue = 0;
        emitter.Gravity.CurrentValue = 0;
        emitter.ParticleSize.CurrentValue = 0;
        emitter.SizeRandom.CurrentValue = 0;
        using var resource = (ParticleEmitter.Resource)emitter.ToResource(
            new CompositionContext(TimeSpan.FromSeconds(1)));

        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
            "precondition: the emitter must still hold alive particles, only sized to zero");

        using var node = new ParticleRenderNode(resource);
        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(rasterization.IsEmpty, Is.True,
            "a zero-sized particle set draws nothing, and the recorded particle-drawable subtree it "
            + "abandoned must not fail the recording");
    }

    [TestCase(DegenerateBrushContent.NoDrawable)]
    [TestCase(DegenerateBrushContent.DisabledDrawable)]
    [TestCase(DegenerateBrushContent.ZeroAreaDrawable)]
    public void DrawableBrush_WithDegenerateContent_DrawsTheOwnerAndFillsNothing(
        DegenerateBrushContent content)
    {
        using Brush.Resource brushResource = CreateDegenerateDrawableBrush(content);
        var pen = new Pen
        {
            Thickness = { CurrentValue = 4 },
            Brush = { CurrentValue = Brushes.White },
            StrokeAlignment = { CurrentValue = StrokeAlignment.Inside },
        };
        using Pen.Resource penResource = pen.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(s_ownerRect, brushResource, penResource);

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(rasterization.IsEmpty, Is.False,
            "the owner still has a stroke to draw, so degenerate brush content must not erase it");
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("A non-empty rasterization must carry a bitmap.");

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(s_ownerRect));
            Assert.That(AlphaAt(bitmap, 2, 2), Is.GreaterThan(0.99f),
                "the owner's stroke must be drawn");
            Assert.That(AlphaAt(bitmap, 16, 12), Is.Zero,
                "content that lowered to nothing must fill nothing, not a fallback colour");
        });
    }

    private static Brush.Resource CreateDegenerateDrawableBrush(DegenerateBrushContent content)
    {
        if (content == DegenerateBrushContent.NoDrawable)
            return (Brush.Resource)new DrawableBrush().ToResource(CompositionContext.Default);

        var drawable = new RectShape();
        drawable.Width.CurrentValue = 18;
        drawable.Height.CurrentValue = 12;
        drawable.Fill.CurrentValue = Brushes.White;
        if (content == DegenerateBrushContent.DisabledDrawable)
        {
            drawable.IsEnabled = false;
        }
        else
        {
            var collapse = new ScaleTransform();
            collapse.Scale.CurrentValue = 0;
            drawable.Transform.CurrentValue = collapse;
        }

        return (Brush.Resource)new DrawableBrush(drawable).ToResource(CompositionContext.Default);
    }

    private static float AlphaAt(Bitmap bitmap, int x, int y)
        => (float)BitConverter.UInt16BitsToHalf(bitmap.GetRow<ushort>(y)[(x * 4) + 3]);

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        return renderer.Rasterize();
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation) => RenderScaleUtilities.MaxBufferDimension;

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
