using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// Backdrop snapshot captures SurfaceDensity and replay un-scales by that, preventing double-scaling.
[NonParallelizable]
[TestFixture]
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

            // 1. Capture a backdrop on a root canvas at SurfaceDensity = s_out.
            using RenderTarget root = RenderTarget.Create(dev, dev)!;
            IBackdrop backdrop;
            using (var canvas = new ImmediateCanvas(root, sOut))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(25, 25, 50, 50), Brushes.Resource.White, null);
                backdrop = canvas.Snapshot();
            }

            // 2. Replay on a separate density-s_out canvas; must un-scale by captured density.
            using RenderTarget nested = RenderTarget.Create(dev, dev)!;
            using (var canvas2 = new ImmediateCanvas(nested, sOut))
            {
                canvas2.Clear(Colors.Black);
                backdrop.Draw(canvas2);
            }

            using Bitmap snap = nested.Snapshot();
            (double cx, double cy, int n) = WhiteCentroid(snap);

            // Logical centre (50,50) -> device (100,100). Double-scaling would push it off-centre.
            Assert.That(n, Is.GreaterThan(2000), "backdrop content missing/clipped — it was drawn too large");
            Assert.That(cx, Is.EqualTo(100.0).Within(5.0), $"backdrop double-scaled horizontally (cx={cx})");
            Assert.That(cy, Is.EqualTo(100.0).Within(5.0), $"backdrop double-scaled vertically (cy={cy})");
        });
    }

    // SnapshotBackdropRenderNode captured on a nested flush canvas (SurfaceDensity = w).
    [Test]
    public void SnapshotBackdropRenderNode_CapturedOnNestedFlushCanvas_NotDoubleScaled()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const float w = 2f;
            const int dev = 200; // ceil(100 logical x w)

            var snapshot = new SnapshotBackdropRenderNode();
            RenderNodeOperation[] captureOps = snapshot.Process(new RenderNodeContext([]));

            // 1. Capture on a flush-style canvas (SurfaceDensity = w).
            using RenderTarget captureTarget = RenderTarget.Create(dev, dev)!;
            using (var capCanvas = new ImmediateCanvas(captureTarget, w))
            {
                capCanvas.Clear(Colors.Black);
                capCanvas.DrawRectangle(new Rect(25, 25, 50, 50), Brushes.Resource.White, null);
                foreach (RenderNodeOperation op in captureOps)
                {
                    op.Render(capCanvas);
                }
            }

            // 2. Replay on a separate density-w canvas.
            using RenderTarget replayTarget = RenderTarget.Create(dev, dev)!;
            using (var replayCanvas = new ImmediateCanvas(replayTarget, w))
            {
                replayCanvas.Clear(Colors.Black);
                snapshot.Draw(replayCanvas);
            }

            using Bitmap snap = replayTarget.Snapshot();
            (double cx, double cy, int n) = WhiteCentroid(snap);

            Assert.That(n, Is.GreaterThan(2000), "backdrop content missing/clipped — captured density was wrong");
            Assert.That(cx, Is.EqualTo(100.0).Within(5.0), $"backdrop double-scaled horizontally (cx={cx})");
            Assert.That(cy, Is.EqualTo(100.0).Within(5.0), $"backdrop double-scaled vertically (cy={cy})");

            foreach (RenderNodeOperation op in captureOps)
            {
                op.Dispose();
            }

            snapshot.Dispose();
        });
    }
}
