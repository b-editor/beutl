using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// End-to-end regressions (raster, GPU-less) for two executor bounds bugs (feature 004 review B1/B2):
/// a shrinking pass whose resolved output is empty must draw nothing rather than pass its input through
/// (B1, a fully-closed <see cref="Clipping"/>), and a non-invariant whole-source fused pass must size and
/// place its output by its declared forward bounds rather than the input rect (B2, <see cref="ColorShift"/>
/// pushing content beyond the input rect).
/// </summary>
[TestFixture]
public class EmptyRoiAndForwardBoundsTests
{
    private static readonly Rect s_input = new(0, 0, 100, 100);

    // ---- B1: fully-closed Clipping draws nothing (through RenderNodeProcessor) ---------------------------

    [Test]
    public void FullyClosedClipping_ThroughProcessor_DrawsNothing()
    {
        var clip = new Clipping();
        clip.Left.CurrentValue = 60;
        clip.Right.CurrentValue = 60;

        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new SourceRectNode(s_input));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        List<Bitmap> bitmaps = processor.Rasterize();
        try
        {
            Assert.That(bitmaps.Count(HasVisibleContent), Is.EqualTo(0),
                "a fully-closed clip must produce no visible output — the input must not pass through");
        }
        finally
        {
            foreach (Bitmap bmp in bitmaps)
                bmp.Dispose();
        }
    }

    // Animation crossing through fully-closed and back: the drop must not corrupt the node's reused plan cache.
    [Test]
    public void ClippingAnimation_ThroughFullyClosedAndBack_DropsOnlyWhileClosed()
    {
        var clip = new Clipping();
        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new SourceRectNode(s_input));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        int RenderVisible(float leftRight)
        {
            clip.Left.CurrentValue = leftRight;
            clip.Right.CurrentValue = leftRight;
            bool updateOnly = false;
            resource.Update(clip, CompositionContext.Default, ref updateOnly);
            node.Update(resource);

            List<Bitmap> bitmaps = processor.Rasterize();
            try
            {
                return bitmaps.Count(HasVisibleContent);
            }
            finally
            {
                foreach (Bitmap bmp in bitmaps)
                    bmp.Dispose();
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(RenderVisible(20f), Is.EqualTo(1), "an open clip produces visible content");
            Assert.That(RenderVisible(60f), Is.EqualTo(0), "a fully-closed clip produces nothing");
            Assert.That(RenderVisible(20f), Is.EqualTo(1), "reopening after a closed frame renders again");
        });
    }

    // ---- B2: ColorShift sizes/places by its declared forward bounds -------------------------------------

    [Test]
    public void ColorShift_BeyondInputRect_HonorsForwardBounds()
    {
        var effect = new ColorShift();
        effect.RedOffset.CurrentValue = new PixelPoint(40, 0);
        effect.AlphaOffset.CurrentValue = new PixelPoint(40, 0);

        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeWhiteRect(s_input)]);

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1));

            // The forward map unions the input with each channel's shifted copy: a +40 x-shift extends the
            // output rect to 140 px wide. The executor must size/place the pass by this, not the 100 px input.
            Assert.That(ops[0].Bounds.Width, Is.EqualTo(140f),
                "the executed output op must carry the declared forward bounds, not the input rect");

            using Bitmap bmp = Rasterize(ops[0]);
            int beyond = 0;
            for (int x = 100; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    if (bmp.SKBitmap.GetPixel(x, y).Alpha != 0)
                        beyond++;
                }
            }

            Assert.That(beyond, Is.GreaterThan(0),
                "content shifted beyond the input rect (x >= 100) was clipped — the buffer was sized to the input");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    private static RenderNodeOperation MakeWhiteRect(Rect bounds)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
            hitTest: bounds.Contains);

    private static bool HasVisibleContent(Bitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.SKBitmap.GetPixel(x, y).Alpha != 0)
                    return true;
            }
        }

        return false;
    }

    private static Bitmap Rasterize(RenderNodeOperation op)
    {
        var size = PixelRect.FromRect(op.Bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: op.Bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }

    private sealed class SourceRectNode(Rect bounds) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
            => [RenderNodeOperation.CreateLambda(
                bounds,
                canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
                hitTest: bounds.Contains)];
    }
}
