using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Regression for the Clipping.AutoCenter "original image drawn in the wrong place" bug. AutoCenter must
// recenter the cropped window while drawing the SAME kept region the in-place crop would. The old code
// blitted at +centeredRect instead of the crop offset, showing the clipped-away corner off-center.
[NonParallelizable]
[TestFixture]
public class ClippingAutoCenterTests
{
    private static readonly PixelSize Frame = new(200, 200);
    private const float Center = 100f;

    private static Drawable.Resource Make(float left, float top, float right, float bottom)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 120;
        shape.Fill.CurrentValue = Brushes.White;
        var c = new Clipping();
        c.Left.CurrentValue = left;
        c.Top.CurrentValue = top;
        c.Right.CurrentValue = right;
        c.Bottom.CurrentValue = bottom;
        c.AutoCenter.CurrentValue = true;
        shape.FilterEffect.CurrentValue = c;
        return shape.ToResource(CompositionContext.Default);
    }

    // Bounding box of the white (content) pixels; throws via the count assertion if nothing was drawn.
    private static (float cx, float cy, int count) ContentCenter(Bitmap bmp)
    {
        int minx = int.MaxValue, miny = int.MaxValue, maxx = int.MinValue, maxy = int.MinValue, n = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.SKBitmap.GetPixel(x, y);
                if (p.Red > 128 && p.Green > 128 && p.Blue > 128)
                {
                    n++;
                    if (x < minx) minx = x;
                    if (x > maxx) maxx = x;
                    if (y < miny) miny = y;
                    if (y > maxy) maxy = y;
                }
            }
        }

        return n == 0 ? (0, 0, 0) : ((minx + maxx) / 2f, (miny + maxy) / 2f, n);
    }

    // AutoCenter must put the kept window's center back on the frame center.
    // Fractional margins also exercise the pointX/pointY sub-pixel rounding path (Clipping.cs:120-136).
    [TestCase(60f, 0f, 0f, 0f, TestName = "AutoCenter_LeftClip")]
    [TestCase(0f, 60f, 0f, 0f, TestName = "AutoCenter_TopClip")]
    [TestCase(0f, 0f, 50f, 0f, TestName = "AutoCenter_RightClip")]
    [TestCase(48f, 24f, 0f, 0f, TestName = "AutoCenter_Corner")]
    [TestCase(60.5f, 0f, 0f, 0f, TestName = "AutoCenter_LeftClip_Fractional")]
    [TestCase(0f, 24.5f, 40.5f, 0f, TestName = "AutoCenter_TopRight_Fractional")]
    public void AutoCenter_CentersKeptRegion(float l, float t, float r, float b)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bmp = GoldenImageHarness.RenderAtScale(Make(l, t, r, b), Frame, 1f);
            (float cx, float cy, int count) = ContentCenter(bmp);

            Assert.That(count, Is.GreaterThan(1000), "almost no content drawn — the source was blitted off-buffer");
            // The kept region is solid white; AutoCenter must center it on the frame.
            Assert.That(cx, Is.EqualTo(Center).Within(2.0),
                $"content not horizontally centered (cx={cx}) — AutoCenter drew the source at the wrong offset");
            Assert.That(cy, Is.EqualTo(Center).Within(2.0),
                $"content not vertically centered (cy={cy}) — AutoCenter drew the source at the wrong offset");
        });
    }

    // Kept-region identity, not just position: a red border distinguishes the KEPT region from the clipped-away
    // one. A left clip removes the LEFT border, so AutoCenter must show the right border centered (white at the
    // kept window's left edge, red at its right edge), not the clipped-away left part.
    private static Drawable.Resource MakeBordered(float left)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 120;
        shape.Fill.CurrentValue = Brushes.White;
        var pen = new Pen();
        pen.Thickness.CurrentValue = 10;
        pen.Brush.CurrentValue = Brushes.Red;
        shape.Pen.CurrentValue = pen;
        var c = new Clipping();
        c.Left.CurrentValue = left;
        c.AutoCenter.CurrentValue = true;
        shape.FilterEffect.CurrentValue = c;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void AutoCenter_KeepsCorrectRegion_NotClippedAwayCorner()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bmp = GoldenImageHarness.RenderAtScale(MakeBordered(60f), Frame, 1f);

            // content bbox over all non-black pixels (white interior + red border)
            int minx = int.MaxValue, maxx = int.MinValue, cyN = 0, cySum = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var p = bmp.SKBitmap.GetPixel(x, y);
                    if (p.Red > 40 || p.Green > 40 || p.Blue > 40)
                    {
                        if (x < minx) minx = x;
                        if (x > maxx) maxx = x;
                        cyN++; cySum += y;
                    }
                }
            }

            Assert.That(maxx - minx, Is.GreaterThan(20), "no content");
            float cx = (minx + maxx) / 2f;
            Assert.That(cx, Is.EqualTo(Center).Within(2.0), $"kept window not centered (cx={cx})");

            int midY = cySum / cyN;
            var leftPx = bmp.SKBitmap.GetPixel(minx + 3, midY);
            var rightPx = bmp.SKBitmap.GetPixel(maxx - 3, midY);
            // KEPT region: the right (kept) border is red; the left edge is the white interior (its border was clipped).
            bool rightIsRed = rightPx.Red > 150 && rightPx.Green < 90 && rightPx.Blue < 90;
            bool leftIsWhite = leftPx.Red > 150 && leftPx.Green > 150 && leftPx.Blue > 150;
            Assert.That(rightIsRed, Is.True, "kept window's right edge is not the red border — wrong region drawn");
            Assert.That(leftIsWhite, Is.True, "kept window's left edge is not the white interior — the clipped-away left border was drawn");
        });
    }

    // AutoClip detects margins in device px and converts to logical via /w.
    private static Drawable.Resource MakeAutoClipChain()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 110;
        shape.Height.CurrentValue = 90;
        shape.Fill.CurrentValue = Brushes.White;

        var expand = new Clipping();
        expand.Left.CurrentValue = -30;
        expand.Top.CurrentValue = -30;
        expand.Right.CurrentValue = -30;
        expand.Bottom.CurrentValue = -30;

        var autoClip = new Clipping();
        autoClip.AutoClip.CurrentValue = true;

        var group = new FilterEffectGroup();
        group.Children.Add(expand);
        group.Children.Add(autoClip);
        shape.FilterEffect.CurrentValue = group;
        return shape.ToResource(CompositionContext.Default);
    }

    [Test]
    public void AutoClip_KeepsLogicalAppearance_AtSupersample()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap r1 = GoldenImageHarness.RenderAtScale(MakeAutoClipChain(), Frame, 1f);
            using Bitmap hi = GoldenImageHarness.RenderAtScale(MakeAutoClipChain(), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);

            double ssim = ImageMetrics.Ssim(r1, delivered);
            TestContext.WriteLine($"AutoClip 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                "AutoClip diverged at s_out>1 — the device-px detected margin was not converted to logical via / w");
        });
    }
}
