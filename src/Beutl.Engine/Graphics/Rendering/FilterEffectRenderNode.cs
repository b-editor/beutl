using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

public class FilterEffectRenderNode(FilterEffect.Resource filterEffect) : ContainerRenderNode
{
    // Keyed on the graphics-context identity when none is resolved yet (the pool-less / no-GPU path), so the cache
    // still functions and a later real context is treated as a change.
    private static readonly object s_noContext = new();

    private readonly PlanCache _planCache = new();

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
        var graphBuilder = new EffectGraphBuilder(
            bounds, context.OutputScale, workingScale, context.MaxWorkingScale);
        resource.GetOriginal().Describe(graphBuilder, resource);
        using EffectGraph graph = graphBuilder.Build();

        // Cache the compiled plan on structural identity (C5): a parameter-only frame (animated uniforms, sigma,
        // filter factories) re-describes and rebinds without recompiling. Structural changes and a
        // graphics-context change miss and recompile exactly once.
        object contextId = GraphicsContextFactory.SharedContext ?? s_noContext;
        StructuralKey key = StructuralKey.Compute(graph);
        CompiledPlan plan;
        if (_planCache.TryGet(key, contextId, out CompiledPlan cached))
        {
            plan = ParameterBlock.Extract(graph).RebindOnto(cached);
        }
        else
        {
            plan = EffectGraphCompiler.Compile(graph, context.Diagnostics);
            _planCache.Store(key, contextId, plan);
        }

        // The parent wants the effect's full output; Rect.Invalid requests every pass's full bounds (no ROI crop).
        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale);
        return PlanExecutor.Execute(
            plan, resources, context.Input, context.OutputScale, workingScale, context.MaxWorkingScale,
            context.Diagnostics, context.Pool);
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _planCache.Invalidate();
    }
}
