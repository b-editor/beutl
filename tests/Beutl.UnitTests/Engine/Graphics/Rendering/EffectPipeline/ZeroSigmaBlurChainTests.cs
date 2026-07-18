using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for zero-sigma blur handling (feature 004). An identity-returning filter factory
/// must not dispose its predecessor. A blur whose clamped sigma is zero keeps its structural pass for plan-cache
/// stability, but the executor detects the all-null filter chain before allocation and passes the source through.
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

        RenderNodeOperation[] ops = Execute(new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery)
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

    // A zero-sigma blur is a true graph identity. Keeping an all-null Skia pass would still allocate and rasterize an
    // intermediate, and could drop an otherwise valid preview solely because that unnecessary allocation failed.
    [TestCase(0f, 0f)]
    [TestCase(-3f, -2f)]
    public void Blur_ClampedZeroSigma_KeepsStructuralPassButExecutesAsIdentity(float sigmaX, float sigmaY)
    {
        using EffectGraph graph = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Blur(new Size(sigmaX, sigmaY)).Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        using EffectGraph nonzeroGraph = new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Blur(new Size(2, 2)).Build();

        Assert.Multiple(() =>
        {
            Assert.That(graph.Nodes, Has.Count.EqualTo(1),
                "the descriptor remains structural so an animated sigma crossing zero does not recompile");
            Assert.That(plan.Passes, Has.Length.EqualTo(1));
            Assert.That(plan.Passes[0], Is.TypeOf<SkiaFilterPass>());
            Assert.That(StructuralKey.Compute(graph), Is.EqualTo(StructuralKey.Compute(nonzeroGraph)),
                "zero and nonzero sigma are parameter changes under the same plan shape");
        });

        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        pool.SetBackingFactoryForTest(static (_, _) => null);
        RenderNodeOperation input = MakeInput();
        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, resources, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics, pool, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(outputs, Is.EqualTo(new[] { input }),
                    "an all-null Skia filter chain passes the original operation through");
                Assert.That(diagnostics.Snapshot().PoolAcquires, Is.Zero,
                    "identity is detected before the pass target is acquired");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
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
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery)
                    .DropShadow(position, sigma, color)
                    .Blur(new Size(0, 0))),
            "a Skia chain whose blur is at σ=0 must render without a use-after-dispose fault");
        shadowOnly = Execute(new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).DropShadow(position, sigma, color));

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
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
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
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: op.Bounds.Size))
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
