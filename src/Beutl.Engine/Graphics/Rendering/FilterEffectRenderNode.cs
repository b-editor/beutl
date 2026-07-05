using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

public class FilterEffectRenderNode(FilterEffect.Resource filterEffect) : ContainerRenderNode
{
    public (FilterEffect.Resource Resource, int Version)? FilterEffect { get; private set; } = filterEffect.Capture();

    public bool Update(FilterEffect.Resource? fe)
    {
        if (!fe.Compare(FilterEffect))
        {
            FilterEffect = fe.Capture();
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (FilterEffect == null || !FilterEffect.Value.Resource.IsEnabled)
        {
            return context.Input;
        }

        // Resolve working scale from the densest concrete input, capped by the global ceiling.
        Span<EffectiveScale> inputScales = context.Input.Length <= 16
            ? stackalloc EffectiveScale[context.Input.Length]
            : new EffectiveScale[context.Input.Length];
        for (int i = 0; i < context.Input.Length; i++)
        {
            inputScales[i] = context.Input[i].EffectiveScale;
        }

        float workingScale = RenderNodeContext.ResolveWorkingScale(
            inputScales, context.OutputScale, context.MaxWorkingScale);

        // Clamp w to keep ceil(bounds * w) within GPU/memory limits.
        Rect bounds = context.CalculateBounds();
        workingScale = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, workingScale);

        FilterEffect.Resource resource = FilterEffect.Value.Resource;
        var graphBuilder = new EffectGraphBuilder(bounds, context.OutputScale, workingScale);
        resource.GetOriginal().Describe(graphBuilder, resource);
        using EffectGraph graph = graphBuilder.Build();

        CompiledPlan plan = EffectGraphCompiler.Compile(graph, context.Diagnostics);
        // The parent wants the effect's full output; Rect.Invalid requests every pass's full bounds (no ROI crop).
        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale);
        return PlanExecutor.Execute(
            plan, resources, context.Input, bounds, context.OutputScale, workingScale, context.MaxWorkingScale,
            context.Diagnostics, context.Pool);
    }
}
