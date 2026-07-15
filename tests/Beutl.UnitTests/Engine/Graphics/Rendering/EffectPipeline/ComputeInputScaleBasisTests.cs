using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Gates the compute-input scale basis (feature 004): <see cref="IComputeContext"/> exposes the source and target
/// logical bounds plus their potentially distinct densities. Dispatches can therefore translate logical coordinates
/// exactly even when the resolver carries a lower <c>w</c> or a custom bounds contract moves the output.
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
            var targetBounds = new Rect(7, 11, 16, 16);
            BoundsContract bounds = BoundsContract.Create(
                static input => new Rect(input.X + 7, input.Y + 11, input.Width, input.Height),
                static output => new Rect(output.X - 7, output.Y - 11, output.Width, output.Height));

            int sourceWidth = -1, sourceHeight = -1;
            float contextScale = float.NaN;
            float sourceScale = float.NaN;
            Rect observedSourceBounds = Rect.Invalid;
            Rect observedTargetBounds = Rect.Invalid;
            var descriptor = ComputeNodeDescriptor.Create(
                ctx =>
                {
                    sourceWidth = ctx.Source.Width;
                    sourceHeight = ctx.Source.Height;
                    contextScale = ctx.WorkingScale;
                    sourceScale = ctx.SourceScale;
                    observedSourceBounds = ctx.SourceBounds;
                    observedTargetBounds = ctx.TargetBounds;
                    ctx.CopySourceToDestination();
                },
                passCount: 1,
                bounds, ComputeFallbackPolicy.Identity);

            var builder = new EffectGraphBuilder(describeBounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
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
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);

            try
            {
                Assert.That(contextScale, Is.LessThan(1f),
                    "precondition: the resolver's per-axis clamp did not engage, so the scenario is vacuous");
                (int expectedWidth, int expectedHeight) = RenderNodeContext.DeviceBufferSize(opBounds, contextScale);
                Assert.That((sourceWidth, sourceHeight), Is.EqualTo((expectedWidth, expectedHeight)),
                    "the materialized compute input's grid must match the pass-resolved coordinate basis (ctx.WorkingScale)");
                Assert.That(sourceScale, Is.EqualTo(contextScale).Within(1e-6f));
                Assert.That(observedSourceBounds, Is.EqualTo(opBounds));
                Assert.That(observedTargetBounds, Is.EqualTo(targetBounds));
            }
            finally
            {
                RenderNodeOperation.DisposeAll(ops);
            }
        });
    }
}
