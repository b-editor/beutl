using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class BlurFilterLayerBoundsTests
{
    private static readonly PixelSize s_frame = new(400, 400);

    [Test]
    public void DropShadow_DoesNotSampleOutsideItsInput()
    {
        AssertFiniteOutput(CreateShadow(), "drop shadow");
    }

    [Test]
    public void Blur_DoesNotSampleOutsideItsInput()
    {
        var blur = new Blur();
        blur.Sigma.Value = new Size(18, 18);
        AssertFiniteOutput(blur, "blur");
    }

    [Test]
    public void ShadowedContentOverAnImage_StaysFiniteAfterAnEarlierRender()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Dirty the device memory the next render will draw into.
            using (Drawable.Resource warmup = CreatePlate().ToResource(CompositionContext.Default))
            using (Bitmap _ = RenderScene(warmup))
            {
            }

            RectShape shape = CreateRectangle(240, 120, Brushes.White);
            shape.FilterEffect.Value = CreateShadow();
            using Drawable.Resource plate = CreatePlate().ToResource(CompositionContext.Default);
            using Drawable.Resource shadowed = shape.ToResource(CompositionContext.Default);
            using Bitmap actual = RenderScene(plate, shadowed);

            AssertAllChannelsFinite(actual, "shadowed content over an image after an earlier render");
        });
    }

    private static SourceImage CreatePlate()
    {
        Uri uri = TestMediaHelper.CreateTestImageUri(s_frame.Width, s_frame.Height, Colors.White);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(uri);
        var image = new SourceImage();
        image.Source.Value = imageSource;
        return image;
    }

    private static void AssertFiniteOutput(FilterEffect effect, string scenario)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RectShape shape = CreateRectangle(240, 120, Brushes.White);
            shape.FilterEffect.Value = effect;
            using Drawable.Resource resource = shape.ToResource(CompositionContext.Default);
            using Bitmap actual = RenderScene(resource);

            AssertAllChannelsFinite(actual, scenario);
        });
    }

    private static DropShadow CreateShadow()
    {
        var shadow = new DropShadow();
        shadow.Position.Value = new Point(0, 10);
        shadow.Sigma.Value = new Size(18, 18);
        shadow.Color.Value = Color.FromArgb(150, 0, 0, 0);
        return shadow;
    }

    private static void AssertAllChannelsFinite(Bitmap bitmap, string scenario)
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                ImageMetrics.FirstNonFinite((scenario, bitmap)),
                Is.Null,
                $"{scenario} produced a non-finite channel value; the filter sampled outside its input.");
            Assert.That(
                ImageMetrics.HasFiniteVisibleContent(bitmap, alphaThreshold: 0.01f),
                Is.True,
                $"{scenario} rendered nothing, so the finiteness check proves nothing.");
        });
    }

    private static RectShape CreateRectangle(float width, float height, Brush fill)
    {
        var shape = new RectShape();
        shape.Width.Value = width;
        shape.Height.Value = height;
        shape.Fill.Value = fill;
        return shape;
    }

    private static Bitmap RenderScene(params Drawable.Resource[] resources)
    {
        using RenderTarget target = RenderTarget.Create(s_frame.Width, s_frame.Height)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1f, logicalSize: s_frame.ToSize(1));
        canvas.Clear();

        using var root = new DrawableRenderNode(resources[0]);
        using (var context = new GraphicsContext2D(root, s_frame.ToSize(1), 1f))
        {
            foreach (Drawable.Resource resource in resources)
                context.DrawDrawable(resource);
        }

        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, s_frame.ToSize(1)),
            OutputScale = 1f,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        renderer.Render(canvas);
        return target.Snapshot();
    }
}
