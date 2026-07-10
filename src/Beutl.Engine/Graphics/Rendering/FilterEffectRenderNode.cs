using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public class FilterEffectRenderNode(FilterEffect.Resource filterEffect) : ContainerRenderNode
{
    // Keyed on the graphics-context identity when none is resolved yet (the pool-less / no-GPU path), so the cache
    // still functions and a later real context is treated as a change.
    private static readonly object s_noContext = new();

    private readonly PlanCache _planCache = new();
    private readonly EffectPrefixCache _prefixCache = new();

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

        // Pass-prefix output caching (C10): reuse a stable leading run of passes so a heavy static prefix (a blur,
        // a stroke) is not re-executed every frame merely because the tail is animated. Only engaged on the pooled
        // render path; the pool-less golden/frozen harnesses render once and never reach the engagement threshold.
        if (context.Pool != null)
        {
            PrefixDecision decision = _prefixCache.Prepare(
                resource, plan, key, contextId, workingScale,
                context.Input, RenderNodeCacheHelper.CanCacheRecursiveChildrenOnly(this));

            switch (decision.Mode)
            {
                case PrefixMode.Resume:
                    return ExecuteResumed(context, plan, resources, workingScale, decision);
                case PrefixMode.Capture:
                    return ExecuteAndCapture(context, plan, resources, workingScale, decision.Pass);
            }
        }

        return PlanExecutor.Execute(
            plan, resources, context.Input, context.OutputScale, workingScale, context.MaxWorkingScale,
            context.Diagnostics, context.Pool);
    }

    // Skips the retained prefix's passes: the cached buffer seeds the pass after it, so passes 0..k neither draw nor
    // allocate. The fresh input ops are discarded (the stable prefix already encapsulates them), counting one hit.
    private RenderNodeOperation[] ExecuteResumed(
        RenderNodeContext context, CompiledPlan plan, FrameResources resources, float workingScale,
        PrefixDecision decision)
    {
        RenderNodeOperation.DisposeAll(context.Input);
        if (context.Diagnostics != null)
            context.Diagnostics.PrefixCacheHits++;

        RenderNodeOperation seed = RenderNodeOperation.CreateFromRenderTarget(
            decision.SeedBounds, decision.SeedBounds.Position, decision.SeedTarget!.ShallowCopy(), decision.SeedScale);
        return PlanExecutor.Execute(
            plan, resources, [seed], context.OutputScale, workingScale, context.MaxWorkingScale,
            context.Diagnostics, context.Pool, startPass: decision.Pass);
    }

    // Full execution that additionally retains the capture pass's output for subsequent frames to resume from.
    private RenderNodeOperation[] ExecuteAndCapture(
        RenderNodeContext context, CompiledPlan plan, FrameResources resources, float workingScale, int capturePass)
    {
        var sink = new PrefixCaptureSink { CapturePassIndex = capturePass };
        RenderNodeOperation[] result = PlanExecutor.Execute(
            plan, resources, context.Input, context.OutputScale, workingScale, context.MaxWorkingScale,
            context.Diagnostics, context.Pool, startPass: 0, captureSink: sink);
        _prefixCache.StoreCaptured(sink, capturePass, plan);
        return result;
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _planCache.Invalidate();
        _prefixCache.Dispose();
    }
}
