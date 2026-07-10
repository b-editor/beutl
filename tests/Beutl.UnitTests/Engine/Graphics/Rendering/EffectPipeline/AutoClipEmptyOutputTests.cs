using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the AutoClip empty-output drop (feature 004): an AutoClip
/// <see cref="Clipping"/> over a fully transparent input must produce NO downstream operation — matching the
/// legacy imperative Apply (which removed the target) and the §C3 empty-output drop rule. Before the fix the
/// render-time geometry pass kept a full-size, all-transparent output target whose operation still hit-tested
/// its whole bounds, so an invisible clip result stayed selectable in the editor.
/// </summary>
[TestFixture]
public class AutoClipEmptyOutputTests
{
    private static readonly Rect s_input = new(0, 0, 100, 100);

    // A fully transparent input yields no non-transparent pixels, so the auto-clip region is empty.
    [Test]
    public void AutoClip_FullyTransparentInput_ProducesNoOperation()
    {
        var clip = new Clipping();
        clip.AutoClip.CurrentValue = true;

        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeTransparentRect(s_input)]);

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(0),
                "a fully transparent auto-clip must produce no downstream operation — an all-transparent, "
                + "full-size output target must not stay hit-testable in the editor");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // The non-empty path still resolves: content present means the pass keeps its (render-time, full-size)
    // output and renders visible pixels — the drop must be exclusive to the all-transparent input.
    [Test]
    public void AutoClip_PartiallyFilledInput_ProducesVisibleOperation()
    {
        var clip = new Clipping();
        clip.AutoClip.CurrentValue = true;

        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var content = new Rect(30, 30, 40, 40);
        var context = new RenderNodeContext([MakeContentRect(s_input, content)]);

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "a non-empty auto-clip produces exactly one output");
            using Bitmap bmp = Rasterize(ops[0]);
            Assert.That(HasVisibleContent(bmp), Is.True, "the kept region must render visible pixels");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    private static RenderNodeOperation MakeTransparentRect(Rect bounds)
        => RenderNodeOperation.CreateLambda(
            bounds,
            _ => { },
            hitTest: _ => false);

    private static RenderNodeOperation MakeContentRect(Rect bounds, Rect content)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(content, Brushes.Resource.White, null),
            hitTest: content.Contains);

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
}
