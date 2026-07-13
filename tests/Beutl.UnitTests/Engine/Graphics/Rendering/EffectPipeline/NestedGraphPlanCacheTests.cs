using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[NonParallelizable]
[TestFixture]
public class NestedGraphPlanCacheTests
{
    private static readonly Rect s_left = new(0, 0, 48, 48);
    private static readonly Rect s_right = new(52, 0, 48, 48);

    [Test]
    public void PersistentNode_CachesOneNestedPlanPerBranchAcrossFrames()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new DelayAnimationEffect
            {
                Effect = { CurrentValue = new Brightness { Amount = { CurrentValue = 120f } } },
            };
            using var resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            using var pool = new RenderTargetPool();
            var diagnostics = new PipelineDiagnostics();

            for (int frame = 0; frame < 5; frame++)
            {
                bool updateOnly = false;
                resource.Update(effect, new CompositionContext(TimeSpan.FromMilliseconds(frame * 16)), ref updateOnly);
                node.Update(resource);
                var context = new RenderNodeContext([Input(s_left), Input(s_right)])
                {
                    Diagnostics = diagnostics,
                    Pool = pool,
                };
                RenderNodeOperation.DisposeAll(node.Process(context));
            }

            Assert.That(diagnostics.Snapshot().PlanCompilations, Is.EqualTo(3),
                "the outer plan and two branch-specific child plans compile once each across all frames");

            var oneBranch = new RenderNodeContext([Input(s_left)])
            {
                Diagnostics = diagnostics,
                Pool = pool,
            };
            RenderNodeOperation.DisposeAll(node.Process(oneBranch));
            Assert.That(diagnostics.Snapshot().PlanCompilations, Is.EqualTo(3),
                "shrinking the runtime branch set reuses branch zero without recompiling");

            var grownAgain = new RenderNodeContext([Input(s_left), Input(s_right)])
            {
                Diagnostics = diagnostics,
                Pool = pool,
            };
            RenderNodeOperation.DisposeAll(node.Process(grownAgain));
            Assert.That(diagnostics.Snapshot().PlanCompilations, Is.EqualTo(4),
                "a pruned branch cache recompiles when that runtime branch returns");
        });
    }

    private static RenderNodeOperation Input(Rect bounds)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
            hitTest: bounds.Contains);
}
