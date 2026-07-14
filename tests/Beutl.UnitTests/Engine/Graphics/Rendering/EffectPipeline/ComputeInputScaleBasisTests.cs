using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Gates the compute-input scale basis (feature 004): <see cref="IComputeContext"/> exposes a single coordinate
/// basis (Width/Height/WorkingScale at the pass-resolved <c>w</c>) and dispatches texelFetch the source with
/// destination-derived coordinates, so the materialized input must bake at that same <c>w</c>. Baking at the
/// boundary working scale shifts the sampling grid whenever the resolver carried a lower <c>w</c> — reachable when
/// describe-time bounds exceed the per-axis budget while the render-time op stays small.
/// </summary>
[NonParallelizable]
[TestFixture]
public class ComputeInputScaleBasisTests
{
    [Test]
    public void ComputeInput_BakesAtPassResolvedScale_MatchingContextBasis()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Describe-time bounds far past the 16384 per-axis budget clamp the resolver's carried working scale
            // below the boundary 1.0; the actual op stays small, so only the pass-resolved w reflects the clamp.
            var describeBounds = new Rect(0, 0, 40000, 16);
            var opBounds = new Rect(0, 0, 16, 16);

            int sourceWidth = -1, sourceHeight = -1;
            float contextScale = float.NaN;
            var descriptor = ComputeNodeDescriptor.Create(
                ctx =>
                {
                    sourceWidth = ctx.Source.Width;
                    sourceHeight = ctx.Source.Height;
                    contextScale = ctx.WorkingScale;
                    ctx.CopySourceToDestination();
                },
                passCount: 1,
                ComputeFallback.Identity);

            var builder = new EffectGraphBuilder(describeBounds, outputScale: 1f, workingScale: 1f);
            builder.Compute(descriptor);
            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                opBounds,
                canvas => canvas.DrawRectangle(opBounds, Brushes.Resource.White, null),
                hitTest: opBounds.Contains);

            RenderNodeOperation[] ops = PlanExecutor.Execute(
                plan, frame, [input], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);

            try
            {
                Assert.That(contextScale, Is.LessThan(1f),
                    "precondition: the resolver's per-axis clamp did not engage, so the scenario is vacuous");
                (int expectedWidth, int expectedHeight) = RenderNodeContext.DeviceBufferSize(opBounds, contextScale);
                Assert.That((sourceWidth, sourceHeight), Is.EqualTo((expectedWidth, expectedHeight)),
                    "the materialized compute input's grid must match the pass-resolved coordinate basis (ctx.WorkingScale)");
            }
            finally
            {
                RenderNodeOperation.DisposeAll(ops);
            }
        });
    }
}
