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
        blur.Sigma.CurrentValue = new Size(18, 18);
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
            shape.FilterEffect.CurrentValue = CreateShadow();
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
        image.Source.CurrentValue = imageSource;
        return image;
    }

    private static void AssertFiniteOutput(FilterEffect effect, string scenario)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RectShape shape = CreateRectangle(240, 120, Brushes.White);
            shape.FilterEffect.CurrentValue = effect;
            using Drawable.Resource resource = shape.ToResource(CompositionContext.Default);
            using Bitmap actual = RenderScene(resource);

            AssertAllChannelsFinite(actual, scenario);
        });
    }

    private static DropShadow CreateShadow()
    {
        var shadow = new DropShadow();
        shadow.Position.CurrentValue = new Point(0, 10);
        shadow.Sigma.CurrentValue = new Size(18, 18);
        shadow.Color.CurrentValue = Color.FromArgb(150, 0, 0, 0);
        return shadow;
    }

    private static void AssertAllChannelsFinite(Bitmap bitmap, string scenario)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int nonFinite = 0;
        int firstIndex = -1;
        for (int i = 0; i < pixels.Length; i++)
        {
            float value = (float)BitConverter.UInt16BitsToHalf(pixels[i]);
            if (float.IsFinite(value))
                continue;

            nonFinite++;
            if (firstIndex < 0)
                firstIndex = i;
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                nonFinite,
                Is.Zero,
                $"{scenario} produced {nonFinite} non-finite channel values "
                + $"(first at pixel {(firstIndex < 0 ? -1 : firstIndex / 4)}); "
                + "the filter sampled outside its input.");
            Assert.That(
                HasVisibleContent(bitmap),
                Is.True,
                $"{scenario} rendered nothing, so the finiteness check proves nothing.");
        });
    }

    private static bool HasVisibleContent(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        for (int i = 3; i < pixels.Length; i += 4)
        {
            float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[i]);
            if (float.IsFinite(alpha) && alpha > 0.01f)
                return true;
        }

        return false;
    }

    private static RectShape CreateRectangle(float width, float height, Brush fill)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = width;
        shape.Height.CurrentValue = height;
        shape.Fill.CurrentValue = fill;
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
