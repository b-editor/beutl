using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Regression for the Clipping.AutoCenter "original image drawn in the wrong place" bug. AutoCenter must
// relocate the CROPPED window to the center of the original bounds while drawing the SAME kept region of
// the source as the in-place crop. The old code blitted the source at +centeredRect instead of the crop
// offset, so it showed the clipped-away corner of the source, off-center. These render an asymmetric clip
// with AutoCenter and assert the kept content lands centered on the frame.
[NonParallelizable]
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

    // left-only / top-only / corner clips: AutoCenter must put the kept window's center back on the frame center.
    [TestCase(60f, 0f, 0f, 0f, TestName = "AutoCenter_LeftClip")]
    [TestCase(0f, 60f, 0f, 0f, TestName = "AutoCenter_TopClip")]
    [TestCase(0f, 0f, 50f, 0f, TestName = "AutoCenter_RightClip")]
    [TestCase(48f, 24f, 0f, 0f, TestName = "AutoCenter_Corner")]
    public void AutoCenter_CentersKeptRegion(float l, float t, float r, float b)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bmp = GoldenImageHarness.RenderAtScale(Make(l, t, r, b), Frame, 1f);
            (float cx, float cy, int count) = ContentCenter(bmp);

            Assert.That(count, Is.GreaterThan(1000), "almost no content drawn — the source was blitted off-buffer");
            // The kept region is solid white; AutoCenter must center it on the frame (was off-center before the fix).
            Assert.That(cx, Is.EqualTo(Center).Within(2.0),
                $"content not horizontally centered (cx={cx}) — AutoCenter drew the source at the wrong offset");
            Assert.That(cy, Is.EqualTo(Center).Within(2.0),
                $"content not vertically centered (cy={cy}) — AutoCenter drew the source at the wrong offset");
        });
    }
}
