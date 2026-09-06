using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class DrawableBrushFractionalContentExtentTests
{
    private static readonly PixelSize Frame = new(256, 144);
    private const float RenderScale = 2f;

    private const float ContentWidth = 45.9f;
    private const float ContentHeight = 30.9f;
    private const float HostWidth = 210f;
    private const float HostHeight = 130f;

    [TestCase(TileMode.None)]
    [TestCase(TileMode.Tile)]
    public void DrawableBrushUniformStretch_FitsFractionalContentBounds(TileMode tileMode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource host = CreateBrushHost(tileMode);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                host, Frame, RenderScale, clearColor: Colors.Transparent);

            PixelRect covered = GetCoveredBounds(rendered);

            float uniformScale = MathF.Min(HostWidth / ContentWidth, HostHeight / ContentHeight);
            float expectedWidth = ContentWidth * uniformScale * RenderScale;

            TestContext.WriteLine(
                $"covered={covered} expected width {expectedWidth:F2} device px "
                + $"(uniform scale {uniformScale:F5})");

            Assert.That(covered.Width, Is.EqualTo(expectedWidth).Within(3d),
                "the fill must be scaled by the content's fractional bounds, not a rounded size");
        });
    }

    [Test]
    public void DrawableBrushUniformStretch_FitsArtworkReachingOutsideTheDestination()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource host = CreateOverreachingBrushHost();
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                host, Frame, RenderScale, clearColor: Colors.Transparent);

            PixelRect covered = GetCoveredBounds(rendered);

            float artworkWidth = ContentWidth * 2f;
            float uniformScale = MathF.Min(HostWidth / artworkWidth, HostHeight / ContentHeight);
            float expectedHeight = ContentHeight * uniformScale * RenderScale;

            TestContext.WriteLine(
                $"covered={covered} expected height {expectedHeight:F2} device px "
                + $"(uniform scale {uniformScale:F5} over artwork width {artworkWidth})");

            Assert.That(
                covered.Height,
                Is.EqualTo(expectedHeight).Within(3d),
                "the fill must be scaled by the artwork's own extent, not by the part inside the "
                + "destination");
        });
    }

    private static Drawable.Resource CreateOverreachingBrushHost()
    {
        var near = new RectShape();
        near.Width.CurrentValue = ContentWidth;
        near.Height.CurrentValue = ContentHeight;
        near.Fill.CurrentValue = Brushes.White;

        var far = new RectShape();
        far.Width.CurrentValue = ContentWidth;
        far.Height.CurrentValue = ContentHeight;
        far.Fill.CurrentValue = Brushes.White;
        far.Transform.CurrentValue = new Beutl.Graphics.Transformation.TranslateTransform(ContentWidth, 0);

        var group = new DrawableGroup();
        group.Children.Add(near);
        group.Children.Add(far);

        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = group;
        brush.Stretch.CurrentValue = Stretch.Uniform;
        brush.TileMode.CurrentValue = TileMode.None;

        var host = new RectShape();
        host.AlignmentX.CurrentValue = AlignmentX.Center;
        host.AlignmentY.CurrentValue = AlignmentY.Center;
        host.Width.CurrentValue = HostWidth;
        host.Height.CurrentValue = HostHeight;
        host.Fill.CurrentValue = brush;
        return host.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateBrushHost(TileMode tileMode)
    {
        var content = new RectShape();
        content.Width.CurrentValue = ContentWidth;
        content.Height.CurrentValue = ContentHeight;
        content.Fill.CurrentValue = Brushes.White;

        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = content;
        brush.Stretch.CurrentValue = Stretch.Uniform;
        brush.TileMode.CurrentValue = tileMode;

        var host = new RectShape();
        host.AlignmentX.CurrentValue = AlignmentX.Center;
        host.AlignmentY.CurrentValue = AlignmentY.Center;
        host.Width.CurrentValue = HostWidth;
        host.Height.CurrentValue = HostHeight;
        host.Fill.CurrentValue = brush;
        return host.ToResource(CompositionContext.Default);
    }

    // Half coverage tracks the geometric edge through the resampling ramp on both sides.
    private static PixelRect GetCoveredBounds(Bitmap bitmap)
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
                float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[(((y * bitmap.Width) + x) * 4) + 3]);
                if (alpha < 0.5f)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        Assert.That(maxX, Is.GreaterThanOrEqualTo(minX), "the drawable-brush fill produced no covered pixels");
        return new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
