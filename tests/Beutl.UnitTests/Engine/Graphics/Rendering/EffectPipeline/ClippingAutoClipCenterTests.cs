using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the AutoClip + AutoCenter combination (feature 004): AutoClip resolves its
/// clip rect only at render time, and when AutoCenter is also set the legacy imperative <c>Clipping.Apply</c>
/// re-centered the kept window inside the input frame (sizing its output to the centered TargetBounds). The
/// render-time path always emitted the un-centered NewBounds, dropping the centering. The fix publishes the
/// centered TargetBounds and shifts the kept region onto it, falling back to NewBounds only when a narrowed buffer
/// cannot contain the centered rect. <see cref="Golden.ClippingAutoCenterTests"/> covers AutoClip and AutoCenter
/// each alone; this covers the combination.
/// </summary>
[NonParallelizable]
[TestFixture]
public class ClippingAutoClipCenterTests
{
    private static readonly Rect s_input = new(0, 0, 100, 100);
    private static readonly Rect s_content = new(30, 30, 40, 40);

    // Un-centered AutoClip tightens to NewBounds = (29,29,40,40) (see AutoClipOutputBoundsTightenTests). AutoCenter
    // re-centers the 40x40 clip rect inside the 100x100 input frame: X = Y = (100 - 40) / 2 = 30.
    private static readonly Rect s_centered = new(30, 30, 40, 40);
    private static readonly Rect s_unCentered = new(29, 29, 40, 40);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // The core regression: AutoClip + AutoCenter emits the centered rect, not the un-centered NewBounds.
    [Test]
    public void AutoClipAutoCenter_TightensOutputBoundsToCenteredRect()
    {
        RenderNodeOperation[] ops = RenderClip(autoCenter: true);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "a non-empty auto-clip produces exactly one output");

            Rect bounds = ops[0].Bounds;
            Assert.Multiple(() =>
            {
                Assert.That(bounds, Is.EqualTo(s_centered),
                    "AutoClip + AutoCenter must emit the centered TargetBounds (legacy Apply semantics)");
                Assert.That(bounds, Is.Not.EqualTo(s_unCentered),
                    "the pre-fix un-centered NewBounds (centering lost) is the regression being restored");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // Kept-region identity: the centered op draws the SAME kept pixels the un-centered AutoClip would, only
    // repositioned. Rasterizing each over its own bounds must yield the same solid content region.
    [Test]
    public void AutoClipAutoCenter_DrawsSameKeptRegionAsUnCentered()
    {
        RenderNodeOperation[] centered = RenderClip(autoCenter: true);
        RenderNodeOperation[] unCentered = RenderClip(autoCenter: false);
        try
        {
            Assert.That(centered, Has.Length.EqualTo(1));
            Assert.That(unCentered, Has.Length.EqualTo(1));

            using Bitmap centeredBmp = Rasterize(centered[0], centered[0].Bounds);
            using Bitmap unCenteredBmp = Rasterize(unCentered[0], unCentered[0].Bounds);

            int centeredWhite = CountWhite(centeredBmp);
            int unCenteredWhite = CountWhite(unCenteredBmp);

            Assert.Multiple(() =>
            {
                Assert.That(centeredWhite, Is.GreaterThan(1000),
                    "the centered kept region is drawn into the buffer (not blitted off-buffer)");
                // Same kept region, differing only by a sub-pixel re-centering offset (fractional vs integer origin).
                Assert.That(centeredWhite, Is.EqualTo(unCenteredWhite).Within(200),
                    "the centered op keeps the same region as the un-centered AutoClip, only repositioned");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(centered);
            RenderNodeOperation.DisposeAll(unCentered);
        }
    }

    private static RenderNodeOperation[] RenderClip(bool autoCenter)
    {
        var clip = new Clipping();
        clip.AutoClip.CurrentValue = true;
        clip.AutoCenter.CurrentValue = autoCenter;

        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeContentRect(s_input, s_content)], RenderIntent.Delivery);
        return node.Process(context);
    }

    private static int CountWhite(Bitmap bmp)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                SKColor p = bmp.SKBitmap.GetPixel(x, y);
                if (p.Red > 200 && p.Green > 200 && p.Blue > 200 && p.Alpha > 200)
                    n++;
            }
        }

        return n;
    }

    private static RenderNodeOperation MakeContentRect(Rect bounds, Rect content)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(content, Brushes.Resource.White, null),
            hitTest: content.Contains);

    private static Bitmap Rasterize(RenderNodeOperation op, Rect window)
    {
        var size = PixelRect.FromRect(window);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: window.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-window.X, -window.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }
}
