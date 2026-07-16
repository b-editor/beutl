using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the C4.2 FlushSyncs accounting across nested-graph siblings: every sibling branch executes an independent
/// child plan over an independent input, so each starts from the parent's entry backend. A sibling that inherited
/// the PREVIOUS sibling's ending backend counted a phantom Vulkan-to-Skia sync at its first consuming pass,
/// inflating N per-branch compute syncs to 2N-1.
/// </summary>
[NonParallelizable]
[TestFixture]
public class NestedGraphSiblingBackendTests
{
    private static readonly Rect s_bounds = new(0, 0, 64, 32);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void NestedComputeSiblings_CountOneSyncPerBranch()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var builder = new EffectGraphBuilder(
                s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
            builder.Split(SplitNodeDescriptor.Static(
                emitter =>
                {
                    emitter.Emit(new Rect(0, 0, 32, 32), session => session.Inputs[0].Draw(session.OpenCanvas()));
                    emitter.Emit(new Rect(32, 0, 32, 32), session => session.Inputs[0].Draw(session.OpenCanvas()));
                },
                branchCount: 2,
                structuralToken: "two-compute-parents"));
            builder.NestedGraph(NestedGraphNodeDescriptor.Create(
                static (branchBuilder, _) => branchBuilder.Compute(ComputeNodeDescriptor.Create(
                    static ctx => ctx.CopySourceToDestination(),
                    passCount: 1,
                    BoundsContract.Create(static r => r, static r => r),
                    ComputeFallbackPolicy.Identity)),
                structuralToken: "compute-per-branch"));

            using EffectGraph graph = builder.Build();
            using var pool = new RenderTargetPool();
            var diagnostics = new PipelineDiagnostics();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

            RenderNodeOperation[] ops = PlanExecutor.Execute(
                plan, frame, [MakeContentRect(s_bounds)], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: diagnostics, pool: pool,
                renderIntent: RenderIntent.Delivery);
            try
            {
                TestContext.WriteLine($"FlushSyncs = {diagnostics.FlushSyncs}");
                Assert.That(diagnostics.FlushSyncs, Is.EqualTo(2),
                    "two sibling compute branches perform one Skia-to-Vulkan sync each; a sibling inheriting the "
                    + "previous sibling's Vulkan state would count a phantom third sync (2N-1)");
            }
            finally
            {
                RenderNodeOperation.DisposeAll(ops);
            }
        });
    }

    private static RenderNodeOperation MakeContentRect(Rect bounds)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
            hitTest: bounds.Contains);
}
