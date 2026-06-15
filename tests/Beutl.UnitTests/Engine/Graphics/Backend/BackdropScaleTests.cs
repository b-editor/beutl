using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// feature 003 (CSM-3): a SourceBackdrop snapshot is the DEVICE-sized backing surface (ceil(frame × density) px).
// A capture records the canvas's SurfaceDensity and the replay un-scales by THAT, not by the replay canvas's own
// density — so a backdrop captured at one density and replayed inside a buffer-flushing FilterEffect (a different
// density) is not double-scaled. The base-CTM model bakes CreateScale(density), so capture and replay both draw
// in LOGICAL coords with no manual prescale.
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

            // 1. Capture a backdrop on a ROOT canvas at SurfaceDensity = s_out: a white square at logical [25,75].
            // The canvas bakes the base CTM CreateScale(s_out), so the rect is drawn in LOGICAL coords; Snapshot
            // records SurfaceDensity = s_out as the capture scale.
            using RenderTarget root = RenderTarget.Create(dev, dev)!;
            IBackdrop backdrop;
            using (var canvas = new ImmediateCanvas(root, sOut))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(25, 25, 50, 50), Brushes.Resource.White, null);
                backdrop = canvas.Snapshot();
            }

            // 2. Replay it on a separate density-s_out canvas (the flush-canvas model: SurfaceDensity = w via the
            // baked base CTM). The capture must un-scale by the density it was CAPTURED at.
            using RenderTarget nested = RenderTarget.Create(dev, dev)!;
            using (var canvas2 = new ImmediateCanvas(nested, sOut))
            {
                canvas2.Clear(Colors.Black);
                backdrop.Draw(canvas2);
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

    // CSM3-1: the production SnapshotBackdropRenderNode path, captured ON a nested flush-style canvas. A buffer-
    // flushing effect renders its input on a canvas whose SurfaceDensity = w (base CTM CreateScale(w)) — see
    // FilterEffectActivator.Flush / RenderNodeProcessor.Rasterize. The captured surface is w-dense, so recording
    // canvas.SurfaceDensity = w lets the replay un-scale by w. Keying off the current Density, which a
    // PushDeviceSpace block lowers to 1, would mis-tag and double-scale it.
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

            // 1. Run the capture op on a nested flush-style canvas: SurfaceDensity = w via the baked base CTM
            // CreateScale(w), so the rect is drawn in LOGICAL coords. The snapshot op records
            // canvas.SurfaceDensity = w as its capture scale.
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

            // 2. Replay it on a separate density-w canvas (same flush-canvas model).
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
