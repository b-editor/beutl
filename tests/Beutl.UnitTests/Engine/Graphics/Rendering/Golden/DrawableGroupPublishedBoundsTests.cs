using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class DrawableGroupPublishedBoundsTests
{
    [Test]
    public void ClippingOnGroup_IsIndependentOfSceneSize()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource group = CreateClippedGroup();
            var smallFrame = new PixelSize(256, 144);
            var largeFrame = new PixelSize(512, 288);
            using Bitmap small = GoldenImageHarness.RenderAtScale(
                group, smallFrame, 1f, clearColor: Colors.Transparent);
            using Bitmap large = GoldenImageHarness.RenderAtScale(
                group, largeFrame, 1f, clearColor: Colors.Transparent);

            LogicalAlphaBounds smallBounds = GetLogicalAlphaBounds(small, smallFrame);
            LogicalAlphaBounds largeBounds = GetLogicalAlphaBounds(large, largeFrame);

            Assert.Multiple(() =>
            {
                Assert.That(smallBounds, Is.EqualTo(largeBounds),
                    "group clipping must be measured from the group bounds, not the scene domain");
                Assert.That(smallBounds.Width, Is.LessThan(200), "the horizontal clipping must be visible");
                Assert.That(smallBounds.Height, Is.LessThan(120), "the vertical clipping must be visible");
            });
        });
    }

    [Test]
    public void DrawableBrush_GroupedContent_MatchesUngroupedStretchFill()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var frame = new PixelSize(220, 160);
            using Drawable.Resource ungrouped = CreateDrawableBrushHost(grouped: false);
            using Drawable.Resource grouped = CreateDrawableBrushHost(grouped: true);
            using Bitmap expected = GoldenImageHarness.RenderAtScale(
                ungrouped, frame, 1f, clearColor: Colors.Transparent);
            using Bitmap actual = GoldenImageHarness.RenderAtScale(
                grouped, frame, 1f, clearColor: Colors.Transparent);

            GoldenImageHarness.AssertByteIdentical(expected, actual);
            LogicalAlphaBounds bounds = GetLogicalAlphaBounds(actual, frame);
            Assert.Multiple(() =>
            {
                Assert.That(bounds.Width, Is.EqualTo(180));
                Assert.That(bounds.Height, Is.EqualTo(120));
            });
        });
    }

    private static Drawable.Resource CreateClippedGroup()
    {
        var clipping = new Clipping();
        clipping.Left.CurrentValue = 60;
        clipping.Top.CurrentValue = 40;
        clipping.Right.CurrentValue = 60;
        clipping.Bottom.CurrentValue = 40;

        var group = new DrawableGroup();
        group.FilterEffect.CurrentValue = clipping;
        group.Children.Add(CreateGradientRectangle(200, 120));
        group.Children.Add(CreateRectangle(60, 60, Brushes.White));
        return group.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateDrawableBrushHost(bool grouped)
    {
        RectShape content = CreateGradientRectangle(60, 40);
        Drawable brushContent;
        if (grouped)
        {
            var group = new DrawableGroup();
            group.Children.Add(content);
            brushContent = group;
        }
        else
        {
            brushContent = content;
        }

        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = brushContent;
        brush.Stretch.CurrentValue = Stretch.Fill;
        brush.TileMode.CurrentValue = TileMode.None;
        brush.DestinationRect.CurrentValue = RelativeRect.Fill;

        return CreateRectangle(180, 120, brush).ToResource(CompositionContext.Default);
    }

    private static RectShape CreateGradientRectangle(float width, float height)
    {
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Colors.Crimson, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Gold, 1));
        return CreateRectangle(width, height, gradient);
    }

    private static RectShape CreateRectangle(float width, float height, Brush fill)
    {
        var rectangle = new RectShape();
        rectangle.Width.CurrentValue = width;
        rectangle.Height.CurrentValue = height;
        rectangle.Fill.CurrentValue = fill;
        return rectangle;
    }

    private static LogicalAlphaBounds GetLogicalAlphaBounds(Bitmap bitmap, PixelSize frame)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[((y * bitmap.Width) + x) * 4 + 3]);
                if (alpha <= 0.01f)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        Assert.That(maxX, Is.GreaterThanOrEqualTo(minX), "the render must contain visible pixels");
        Assert.That(maxY, Is.GreaterThanOrEqualTo(minY), "the render must contain visible pixels");
        return new LogicalAlphaBounds(
            (minX * 2) - frame.Width,
            (minY * 2) - frame.Height,
            maxX - minX + 1,
            maxY - minY + 1);
    }

    private readonly record struct LogicalAlphaBounds(
        int TwiceLeftFromFrameCenter,
        int TwiceTopFromFrameCenter,
        int Width,
        int Height);
}
