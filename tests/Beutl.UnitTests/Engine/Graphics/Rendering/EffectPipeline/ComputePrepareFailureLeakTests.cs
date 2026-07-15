using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the compute-input sampling-preparation leak (feature 004, C7): when a Vulkan compute pass has
/// materialized its input and <c>PrepareForSampling</c> throws (a layout-transition/context-loss failure), the op
/// was already detached from the working set and the materialized pooled buffer was not yet inside any cleanup
/// scope — both must still be released on the way out. The throw is injected through a test seam because a real
/// layout-transition failure is not forcible from a test.
/// </summary>
[NonParallelizable]
[TestFixture]
public class ComputePrepareFailureLeakTests
{
    [Test]
    public void ComputeInput_PrepareForSamplingThrows_ReleasesMaterializedInputLease()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 16, 16);
            var effect = new PixelSortEffect();
            effect.Direction.CurrentValue = PixelSortDirection.Horizontal;
            effect.SortKey.CurrentValue = PixelSortKey.Luminance;
            var resource = (FilterEffect.Resource)effect.ToResource(new CompositionContext(TimeSpan.Zero));

            var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f);
            effect.Describe(builder, resource);
            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);

            using var pool = new RenderTargetPool();
            var cleanupFailure = new InvalidOperationException("simulated source-operation cleanup failure");
            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                bounds,
                canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
                hitTest: bounds.Contains,
                onDispose: () => throw cleanupFailure);

            var injected = new InvalidOperationException("simulated layout-transition failure");
            PlanExecutor.ForceComputePrepareFailureForTests(injected);
            try
            {
                InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() =>
                    PlanExecutor.Execute(
                        plan, frame, [input], outputScale: 1f, workingScale: 1f,
                        maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool));
                Assert.That(thrown, Is.SameAs(injected),
                    "the prepare failure surfaces unwrapped even when source-operation cleanup also fails");
            }
            finally
            {
                PlanExecutor.ResetComputePrepareFailureForTests();
            }

            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
                "the PrepareForSampling throw stranded the materialized compute input's pooled lease");
        });
    }

    [Test]
    public void ComputeOutput_PrepareForWriteThrows_PreservesFailureAndReleasesEveryInput()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 16, 16);
            var effect = new PixelSortEffect();
            effect.Direction.CurrentValue = PixelSortDirection.Horizontal;
            effect.SortKey.CurrentValue = PixelSortKey.Luminance;
            var resource = (FilterEffect.Resource)effect.ToResource(new CompositionContext(TimeSpan.Zero));

            var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f);
            effect.Describe(builder, resource);
            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);

            using var pool = new RenderTargetPool();
            var cleanupFailure = new InvalidOperationException("simulated source-operation cleanup failure");
            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                bounds,
                canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
                hitTest: bounds.Contains,
                onDispose: () => throw cleanupFailure);

            var injected = new InvalidOperationException("simulated compute write-preparation failure");
            PlanExecutor.ForceComputeOutputPrepareFailureForTests(injected);
            try
            {
                InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() =>
                    PlanExecutor.Execute(
                        plan, frame, [input], outputScale: 1f, workingScale: 1f,
                        maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool));
                Assert.That(thrown, Is.SameAs(injected),
                    "the write-preparation failure must survive a source-operation cleanup failure");
            }
            finally
            {
                PlanExecutor.ResetComputeOutputPrepareFailureForTests();
            }

            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
                "write-preparation cleanup must release both the output and materialized input leases");
        });
    }
}
