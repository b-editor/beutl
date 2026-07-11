using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the zero-sigma blur use-after-dispose (feature 004). A Skia-filter factory that
/// returns its own argument (a no-op — the shape a blur takes at σ = 0) made the executor dispose that filter and
/// then draw with it. The fix is two-fold: the executor only advances when a factory produced a genuinely new
/// instance (so any identity-returning factory is safe), and the zero-sigma blur factory returns null (a no-op)
/// rather than its argument. Both halves are pinned here.
/// </summary>
[TestFixture]
public class ZeroSigmaBlurChainTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    // The executor guard: a factory that returns its argument must NOT cause the predecessor filter to be disposed
    // while it is still in use. A middle identity stage returns its input; a following probe stage reads that input's
    // native handle. Before the fix the executor disposed the identity's returned filter (its handle went to zero) and
    // then handed the freed filter to the probe stage; the guard keeps it live.
    [Test]
    public void ExecuteSkia_IdentityFactory_DoesNotDisposeTheFilterStillInUse()
    {
        IntPtr observedInnerHandle = new(-1);
        var blur = SkiaFilterNodeDescriptor.Create(
            static inner => SKImageFilter.CreateBlur(3, 3, inner),
            BoundsContract.Create(static r => r.Inflate(9), static r => r.Inflate(9)),
            structuralToken: "chain-blur");
        var identity = SkiaFilterNodeDescriptor.Create(
            static inner => inner, BoundsContract.Create(static r => r, static r => r), "chain-identity");
        var probe = SkiaFilterNodeDescriptor.Create(
            inner =>
            {
                observedInnerHandle = inner?.Handle ?? IntPtr.Zero;
                return SKImageFilter.CreateBlur(1, 1, inner);
            },
            BoundsContract.Create(static r => r.Inflate(3), static r => r.Inflate(3)),
            structuralToken: "chain-probe");

        RenderNodeOperation[] ops = Execute(new EffectGraphBuilder(s_bounds, 1f, 1f)
            .SkiaFilter(blur).SkiaFilter(identity).SkiaFilter(probe));
        try
        {
            Assert.That(observedInnerHandle, Is.Not.EqualTo(IntPtr.Zero),
                "the identity stage's returned filter must still be live when the next stage composes over it");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // The authoring fix: a zero-sigma blur is a no-op, so its factory must return null (not its argument), leaving the
    // accumulated filter unchanged. Returning the argument was what tripped the executor's dispose-predecessor step.
    [Test]
    public void Blur_ZeroSigmaFactory_ReturnsNull_NotItsArgument()
    {
        using EffectGraph graph = new EffectGraphBuilder(s_bounds, 1f, 1f).Blur(new Size(0, 0)).Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        var pass = (SkiaFilterPass)plan.Passes[0];

        using SKImageFilter probe = SKImageFilter.CreateBlur(2, 2);
        SKImageFilter? result = pass.Filters[0](probe);

        Assert.That(result, Is.Null,
            "a zero-sigma blur factory is a no-op: it must return null, never the inner filter it was handed");
    }

    // End-to-end smoke: a DropShadow whose trailing blur is at σ=0 renders without faulting and matches DropShadow
    // alone (the σ=0 blur inflates by 0, so bounds and pixels are identical).
    [Test]
    public void DropShadowThenZeroSigmaBlur_RendersWithoutThrowing_MatchesDropShadowAlone()
    {
        var position = new Point(12, 12);
        var sigma = new Size(3, 3);
        Color color = Colors.Black;

        RenderNodeOperation[] withBlur = null!;
        RenderNodeOperation[] shadowOnly = null!;
        Assert.DoesNotThrow(
            () => withBlur = Execute(
                new EffectGraphBuilder(s_bounds, 1f, 1f)
                    .DropShadow(position, sigma, color)
                    .Blur(new Size(0, 0))),
            "a Skia chain whose blur is at σ=0 must render without a use-after-dispose fault");
        shadowOnly = Execute(new EffectGraphBuilder(s_bounds, 1f, 1f).DropShadow(position, sigma, color));

        try
        {
            Assert.That(withBlur, Has.Length.EqualTo(1));
            Assert.That(shadowOnly, Has.Length.EqualTo(1));
            Assert.That(withBlur[0].Bounds, Is.EqualTo(shadowOnly[0].Bounds),
                "a zero-sigma blur inflates bounds by 0, so the chain output bounds equal DropShadow alone");

            using Bitmap actual = Rasterize(withBlur[0]);
            using Bitmap expected = Rasterize(shadowOnly[0]);
            double ssim = ImageMetrics.Ssim(expected, actual);
            double mae = ImageMetrics.MeanAbsoluteError(expected, actual);
            TestContext.WriteLine($"SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM {ssim}");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(withBlur);
            RenderNodeOperation.DisposeAll(shadowOnly);
        }
    }

    private static RenderNodeOperation[] Execute(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, res, [MakeInput()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
    }

    private static RenderNodeOperation MakeInput()
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds.Deflate(20), Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);

    private static Bitmap Rasterize(RenderNodeOperation op)
    {
        var size = PixelRect.FromRect(op.Bounds);
        using RenderTarget target = RenderTarget.Create(Math.Max(1, size.Width), Math.Max(1, size.Height))
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
