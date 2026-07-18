using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Gates the maxDimension carry (feature 004, FR-037(b)): the per-axis cap a caller passes to
/// <see cref="EffectGraphCompiler.ResolveResources"/> must also bound the executor's render-time re-clamps.
/// Before the carry, those re-clamps used the global default, so a render-time-grown op (a dynamic predecessor
/// emitting wider bounds than resolve time saw) could allocate buffers past the caller's cap.
/// </summary>
[NonParallelizable]
[TestFixture]
public class MaxDimensionCarryTests
{
    private static readonly Rect s_describeBounds = new(0, 0, 64, 64);

    // An op 256 px wide against a 64 px cap: the invariant fused pass re-derives its output bounds from the op, so
    // the executor's re-clamp — not ResolveResources — is what must apply the cap (64 / 256 = 0.25).
    [Test]
    public void Execute_OpWiderThanResolveTimeBounds_ReclampsWithCarriedMaxDimension()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            CompiledPlan plan = CompileGamma();
            FrameResources frame = EffectGraphCompiler.ResolveResources(
                plan, Rect.Invalid, workingScale: 1f, maxDimension: 64);

            var grown = new Rect(0, 0, 256, 64);
            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                grown,
                canvas => canvas.DrawRectangle(grown, Brushes.Resource.White, null),
                hitTest: _ => false,
                effectiveScale: EffectiveScale.At(1f));

            RenderNodeOperation[] ops = PlanExecutor.Execute(
                plan, frame, [input], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);

            try
            {
                Assert.That(ops, Has.Length.EqualTo(1));
                Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(0.25f).Within(1e-4),
                    "the render-time re-clamp ignored the caller's maxDimension (expected 64 / 256 = 0.25)");
            }
            finally
            {
                RenderNodeOperation.DisposeAll(ops);
            }
        });
    }

    [Test]
    public void ResolveResources_RecordsMaxDimension_OnFrameResources()
    {
        CompiledPlan plan = CompileGamma();
        FrameResources frame = EffectGraphCompiler.ResolveResources(
            plan, Rect.Invalid, workingScale: 1f, maxDimension: 64);

        Assert.That(frame.MaxDimension, Is.EqualTo(64));
    }

    private static CompiledPlan CompileGamma()
    {
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 150f;
        var resource = (FilterEffect.Resource)gamma.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_describeBounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        gamma.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }
}
