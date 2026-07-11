using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the nested-graph working-scale carry (execution-plan §C3.2): a <see cref="NestedGraphPass"/> branch must
/// build, resolve, and execute at the carried working scale of the op feeding it (the FR-012 min-carry), not the
/// raw outer working scale. When an upstream clamped op supplies pixels below the boundary working scale, the
/// branch's materialized output must inherit that reduced density. Before the fix
/// (<see cref="PlanExecutor"/>.ExecuteNestedGraph) threaded the outer <c>workingScale</c> straight into the branch
/// builder / resolve / recurse, so the branch re-materialized above the density the upstream op already reduced.
/// </summary>
[NonParallelizable]
[TestFixture]
public class NestedGraphCarriedScaleTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    [Test]
    public void NestedGraphBranch_InheritsCarriedScale_NotOuterWorkingScale()
    {
        // The op feeding the nested graph carries a density of 0.5, below the boundary working scale of 1.0.
        const float outerWorkingScale = 1.0f;
        const float carriedScale = 0.5f;

        // A zero-delay DelayAnimationEffect wrapping an invariant colour-filter child: a single-input pass describes
        // the child directly through a NestedGraphPass, and the child's fused invariant pass stamps the branch's
        // resolved density onto its output op.
        var blend = new BlendEffect
        {
            Brush = { CurrentValue = new SolidColorBrush(Colors.White) },
            BlendMode = { CurrentValue = BlendMode.SrcOver },
        };
        var delay = new DelayAnimationEffect
        {
            Delay = { CurrentValue = 0f },
            Effect = { CurrentValue = blend },
        };

        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: outerWorkingScale);
        delay.Describe(builder, (FilterEffect.Resource)(object)delay.ToResource(CompositionContext.Default));
        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, outerWorkingScale);

        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains,
            effectiveScale: EffectiveScale.At(carriedScale));

        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: outerWorkingScale,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "a single-input nested graph produces one branch output");
            EffectiveScale scale = ops[0].EffectiveScale;
            Assert.Multiple(() =>
            {
                Assert.That(scale.IsUnbounded, Is.False, "the materialized branch output carries a concrete density");
                Assert.That(scale.Value, Is.EqualTo(carriedScale).Within(1e-4f),
                    "the branch must resolve at the carried scale (min(outer, op density) = 0.5), not the outer 1.0");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }
}
