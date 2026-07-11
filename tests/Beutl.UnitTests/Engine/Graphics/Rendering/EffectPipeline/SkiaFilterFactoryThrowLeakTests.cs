using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the Skia-filter factory-loop exception-path filter leak: <c>ExecuteSkia</c> runs
/// the factory loop that folds the pass's filters into one <see cref="SKImageFilter"/> chain BEFORE the try/finally that
/// releases the composed filter. A LATER factory throwing stranded the chain accumulated by the earlier factories,
/// because the guard did not yet cover the loop. Two consecutive Skia-filter nodes fold into one pass with two
/// factories; the first builds a real filter (captured), the second throws. Hoisting the guard over the loop releases
/// the accumulated chain on that path; the assertion reads the captured filter's handle (SkiaSharp zeroes it on
/// dispose) and the pooled lease count.
/// </summary>
[NonParallelizable]
[TestFixture]
public class SkiaFilterFactoryThrowLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void SkiaFilter_LaterFactoryThrows_DisposesAccumulatedChainAndReleasesPooledLease()
    {
        using var pool = new RenderTargetPool();

        RenderTarget target = pool.Acquire(32, 32)
            ?? throw new InvalidOperationException("RenderTargetPool.Acquire returned null (raster surface unavailable).");
        Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "sanity: the input operation holds one pooled lease");

        SKImageFilter? captured = null;

        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            render: canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains,
            onDispose: target.Dispose,
            effectiveScale: EffectiveScale.At(1f));

        // Two consecutive Skia filters fold into one SkiaFilterPass with two factories. The first produces a real image
        // filter (captured), the second throws mid-loop, so the accumulated chain leaks unless the guard covers the loop.
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        builder.SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => captured = SKImageFilter.CreateBlur(2f, 2f, inner),
            InflateContract(new Thickness(6)),
            structuralToken: "skia-filter-factory-first"));
        builder.SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => throw new InvalidOperationException("simulated later skia-filter factory failure"),
            InflateContract(new Thickness(6)),
            structuralToken: "skia-filter-factory-throw"));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool));

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null, "the first factory must have produced a filter before the second threw");
            Assert.That(captured!.Handle, Is.EqualTo(IntPtr.Zero),
                "a later factory throw must still dispose the chain accumulated by the earlier factories");
            Assert.That(pool.LiveLeaseCount, Is.Zero,
                "the source op and its pooled buffer must be released on the factory-throw path");
        });
    }

    private static BoundsContract InflateContract(Thickness inflate)
        => BoundsContract.Create(rect => rect.Inflate(inflate), rect => rect.Inflate(inflate));
}
