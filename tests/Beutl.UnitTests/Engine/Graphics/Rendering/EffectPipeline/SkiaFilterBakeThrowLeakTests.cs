using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the Skia-filter bake exception-path filter leak: <c>ExecuteSkia</c> builds the
/// composed <see cref="SKImageFilter"/> and only disposes it AFTER <c>BakeSource</c> returns. Disposing the owning
/// <see cref="SKPaint"/> does not dispose its image filter, so a bake throw (the source op's own render failing) skipped
/// the filter disposal and stranded the native handle. Wrapping the bake in try/finally releases the filter on both
/// paths; the assertion reads the captured filter's handle (SkiaSharp zeroes it on dispose) and the pooled lease count.
/// </summary>
[NonParallelizable]
[TestFixture]
public class SkiaFilterBakeThrowLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void SkiaFilter_SourceBakeThrows_DisposesFilterAndReleasesPooledLease()
    {
        using var pool = new RenderTargetPool();

        RenderTarget target = pool.Acquire(32, 32)
            ?? throw new InvalidOperationException("RenderTargetPool.Acquire returned null (raster surface unavailable).");
        Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "sanity: the input operation holds one pooled lease");

        // The single filter node hands its created image filter through a capture so the test can observe its disposal;
        // ExecuteSkia builds it from the factory BEFORE it bakes the source, so it is always created when the bake throws.
        SKImageFilter? captured = null;

        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            render: _ => throw new InvalidOperationException("simulated render failure during the skia-filter bake"),
            hitTest: s_bounds.Contains,
            onDispose: target.Dispose,
            effectiveScale: EffectiveScale.At(1f));

        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner =>
            {
                captured = SKImageFilter.CreateBlur(2f, 2f, inner);
                return captured;
            },
            InflateContract(new Thickness(6)),
            structuralToken: "skia-filter-bake-throw"));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);

        Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null, "the filter factory must have run before the bake threw");
            Assert.That(captured!.Handle, Is.EqualTo(IntPtr.Zero),
                "a bake throw must still dispose the composed image filter (SkiaSharp zeroes the handle on dispose)");
            Assert.That(pool.LiveLeaseCount, Is.Zero,
                "the source op and its pooled buffer must be released on the bake-throw path");
        });
    }

    // A backward contract inflating by the given thickness, mirroring the builder's blur node so the filtered pass sizes
    // its buffer like a real blur.
    private static BoundsContract InflateContract(Thickness inflate)
        => BoundsContract.Create(rect => rect.Inflate(inflate), rect => rect.Inflate(inflate));
}
