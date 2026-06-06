using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// feature 003 (CSM-3): a SourceBackdrop snapshot is the DEVICE-sized backing surface (ceil(frame × s_out) px).
// When the backdrop is replayed inside a buffer-flushing FilterEffect, Draw runs on a NESTED ImmediateCanvas
// whose OutputScale is the default 1f (only the root canvas carries s_out), under a CreateScale(w) CTM. If the
// un-scale decision keys off the DRAW-time canvas.OutputScale, the device capture is blitted 1:1 and then
// scaled by w => the backdrop renders ~s_out× too large and clips. The capture must be un-scaled by the scale
// it was CAPTURED at, regardless of the replay canvas. This reproduces the bug via the TmpBackdrop path.
[NonParallelizable]
public class BackdropScaleTests
{
    private static (double cx, double cy, int n) WhiteCentroid(Bitmap bmp)
    {
        double cx = 0, cy = 0;
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.SKBitmap.GetPixel(x, y);
                if (p.Red > 150 && p.Green > 150 && p.Blue > 150) { n++; cx += x; cy += y; }
            }
        }

        return (n == 0 ? 0 : cx / n, n == 0 ? 0 : cy / n, n);
    }

    [Test]
    public void SnapshotBackdrop_ReplayedInFlush_NotDoubleScaled()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const float sOut = 2f;
            const int dev = 200; // ceil(100 logical × 2)

            // 1. Capture a backdrop on a ROOT canvas at OutputScale = s_out: a white square at logical [25,75].
            using RenderTarget root = RenderTarget.Create(dev, dev)!;
            IBackdrop backdrop;
            using (var canvas = new ImmediateCanvas(root) { OutputScale = sOut })
            {
                canvas.Clear(Colors.Black);
                using (canvas.PushTransform(Matrix.CreateScale(sOut, sOut)))
                {
                    canvas.DrawRectangle(new Rect(25, 25, 50, 50), Brushes.Resource.White, null);
                }

                backdrop = canvas.Snapshot();
            }

            // 2. Replay it inside a flush-like NESTED canvas: OutputScale defaults to 1, CTM prescaled by w == s_out.
            using RenderTarget nested = RenderTarget.Create(dev, dev)!;
            using (var canvas2 = new ImmediateCanvas(nested))
            {
                canvas2.Clear(Colors.Black);
                using (canvas2.PushTransform(Matrix.CreateScale(sOut, sOut)))
                {
                    backdrop.Draw(canvas2);
                }
            }

            using Bitmap snap = nested.Snapshot();
            (double cx, double cy, int n) = WhiteCentroid(snap);

            // The white square's logical centre is (50,50) -> device (100,100) at any scale. A double-scaled
            // backdrop pushes it toward the bottom-right and clips it.
            Assert.That(n, Is.GreaterThan(2000), "backdrop content missing/clipped — it was drawn too large");
            Assert.That(cx, Is.EqualTo(100.0).Within(5.0), $"backdrop double-scaled horizontally (cx={cx})");
            Assert.That(cy, Is.EqualTo(100.0).Within(5.0), $"backdrop double-scaled vertically (cy={cy})");
        });
    }
}
