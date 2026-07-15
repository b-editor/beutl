using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// A negative <see cref="Clipping"/> component expands the output OUTSIDE with a transparent margin instead of
/// cropping. The AutoClip path resolves its margins at render time inside a shrink-only
/// <see cref="GeometrySession.SetOutputBounds"/> buffer, so without a describe-time grow allowance a net-negative
/// margin threw <c>ArgumentException</c> and failed the whole frame (the properties are unconstrained animatable
/// floats — an animation crossing zero hits this transiently).
/// </summary>
[NonParallelizable]
[TestFixture]
public class ClippingNegativeMarginTests
{
    private static readonly Rect s_input = new(0, 0, 100, 100);
    private static readonly Rect s_content = new(30, 30, 40, 40);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // AutoClip detects margins (30,30,31,31) for the 40x40 content; Left = -50 makes the effective left margin
    // 30 - 50 = -20, so the kept rect extends 20 px left of the input: NewBounds = (-20, 29, 89, 39) (top keeps the
    // legacy leading-edge -1 shift; width = 100 - (-20) - 31, height = 100 - 30 - 31).
    [Test]
    public void AutoClip_NegativeLeft_ExpandsOutputWithTransparentMargin()
    {
        var expected = new Rect(-20, 29, 89, 39);

        RenderNodeOperation[] ops = RenderClipping(clip =>
        {
            clip.AutoClip.CurrentValue = true;
            clip.Left.CurrentValue = -50;
        });
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "a negative AutoClip margin must render, not throw");
            Rect bounds = ops[0].Bounds;
            Assert.That(bounds, Is.EqualTo(expected),
                "the output must expand left of the detected content box by the negative margin");

            using Bitmap bmp = Rasterize(ops[0], bounds);
            Assert.Multiple(() =>
            {
                Assert.That(PixelAt(bmp, bounds, new Point(-10, 45)).Alpha, Is.Zero,
                    "the expanded strip left of the input must be a transparent blank margin");
                Assert.That(PixelAt(bmp, bounds, new Point(45, 45)).Alpha, Is.Not.Zero,
                    "the kept content must survive the expansion unchanged");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // All-sides negative expansion stays inside the input: margins (30,30,31,31) - 10 each side keep a positive
    // effective margin, so the box just loosens by 10 px per side around the content: (19, 19, 59, 59).
    [Test]
    public void AutoClip_UniformNegative_LoosensDetectedBoxBySetAmount()
    {
        var expected = new Rect(19, 19, 59, 59);

        RenderNodeOperation[] ops = RenderClipping(clip =>
        {
            clip.AutoClip.CurrentValue = true;
            clip.Left.CurrentValue = -10;
            clip.Top.CurrentValue = -10;
            clip.Right.CurrentValue = -10;
            clip.Bottom.CurrentValue = -10;
        });
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1));
            Assert.That(ops[0].Bounds, Is.EqualTo(expected),
                "a uniform negative margin must loosen the auto-clipped box by exactly that amount per side");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    [Test]
    public void AutoClip_FractionalNegativeLeadingMargin_StaysInsideDeclaredGrowAllowance()
    {
        RenderNodeOperation[] ops = RenderClipping(clip =>
        {
            clip.AutoClip.CurrentValue = true;
            clip.Left.CurrentValue = -50.25f;
            clip.Top.CurrentValue = -20.5f;
        });
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1),
                "fractional leading-edge rounding must not push SetOutputBounds beyond the allocated grow allowance");
            Assert.Multiple(() =>
            {
                Assert.That(ops[0].Bounds.Left, Is.LessThan(0));
                Assert.That(ops[0].Bounds.Top, Is.LessThan(s_content.Top));
            });

            using Bitmap bmp = Rasterize(ops[0], ops[0].Bounds);
            Assert.That(PixelAt(bmp, ops[0].Bounds, new Point(45, 45)).Alpha, Is.Not.Zero,
                "the fractional expansion must preserve the detected content");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // The fixed (non-AutoClip) path: Left = -10 inflates the output rect directly (legacy Apply expanded its target),
    // with the new strip transparent.
    [Test]
    public void FixedClip_NegativeLeft_ExpandsOutputWithTransparentMargin()
    {
        var expected = new Rect(-10, 0, 110, 100);

        RenderNodeOperation[] ops = RenderClipping(clip => clip.Left.CurrentValue = -10);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1));
            Rect bounds = ops[0].Bounds;
            Assert.That(bounds, Is.EqualTo(expected),
                "a fixed negative margin must expand the output rect outward");

            using Bitmap bmp = Rasterize(ops[0], bounds);
            Assert.Multiple(() =>
            {
                Assert.That(PixelAt(bmp, bounds, new Point(-5, 50)).Alpha, Is.Zero,
                    "the expanded strip must be a transparent blank margin");
                Assert.That(PixelAt(bmp, bounds, new Point(45, 45)).Alpha, Is.Not.Zero,
                    "the content must survive the expansion unchanged");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    private static RenderNodeOperation[] RenderClipping(Action<Clipping> configure)
    {
        var clip = new Clipping();
        configure(clip);

        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeContentRect(s_input, s_content)], RenderIntent.Delivery);
        return node.Process(context);
    }

    private static RenderNodeOperation MakeContentRect(Rect bounds, Rect content)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(content, Brushes.Resource.White, null),
            hitTest: content.Contains);

    private static SkiaSharp.SKColor PixelAt(Bitmap bmp, Rect window, Point logical)
        => bmp.SKBitmap.GetPixel((int)(logical.X - window.X), (int)(logical.Y - window.Y));

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
