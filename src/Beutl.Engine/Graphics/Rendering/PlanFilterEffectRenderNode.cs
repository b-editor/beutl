using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

// The default filter-effect render node (produced by FilterEffect.Resource.RenderNodeFactory): runs the compiled-plan
// execution pipeline (describe -> PlanCache -> ParameterBlock rebind -> ResolveResources -> PlanExecutor) and owns the
// per-node plan/prefix caches. Sealed and internal — a plugin that needs a different working scale overrides
// RenderNodeFactory to build its own FilterEffectRenderNode subclass and reimplements Process (without these caches).
internal sealed class PlanFilterEffectRenderNode(FilterEffect.Resource filterEffect) : FilterEffectRenderNode(filterEffect)
{
    // Keyed on the graphics-context identity when none is resolved yet (the pool-less / no-GPU path), so the cache
    // still functions and a later real context is treated as a change.
    private static readonly object s_noContext = new();

    private readonly PlanCache _planCache = new();
    private readonly EffectPrefixCache _prefixCache = new();

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (FilterEffect == null || !FilterEffect.Value.Resource.IsEnabled)
        {
            // A disabled/removed effect bypasses execution, but a prefix retained from an earlier enabled frame would
            // stay pinned outside every pool budget until node dispose. Release it (idempotent); a later re-enable
            // re-warms from scratch.
            _prefixCache.Release();
            return context.Input;
        }

        try
        {
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

            Rect bounds = context.CalculateBounds();

            FilterEffect.Resource resource = FilterEffect.Value.Resource;
            // A Describe that registers a native sampler/child shader and then throws would strand it (ownership only
            // transfers to the graph at Build); the engine aborts the still-open builder in the finally path.
            var graphBuilder = new EffectGraphBuilder(
                bounds, context.OutputScale, workingScale, context.MaxWorkingScale);
            try
            {
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

                FrameResources resources = EffectGraphCompiler.ResolveResources(plan, context.RequestedBounds, workingScale);

                // Pass-prefix output caching (C10): reuse a stable leading run of passes so a heavy static prefix (a blur,
                // a stroke) is not re-executed every frame merely because the tail is animated. Only engaged on the pooled
                // render path with render caching enabled — a caller that disabled render caching (a delivery render) must
                // not have frames served from a retained prefix; the pool-less golden/frozen harnesses render once and never
                // reach the engagement threshold.
                if (context.Pool != null && context.IsRenderCacheEnabled)
                {
                    PrefixDecision decision = _prefixCache.Prepare(
                        resource, plan, key, contextId, workingScale, context.OutputScale, context.MaxWorkingScale,
                        context.Input, RenderNodeCacheHelper.CanCacheRecursiveChildrenOnly(this), resources);

                    switch (decision.Mode)
                    {
                        case PrefixMode.Resume:
                            return ExecuteResumed(context, plan, resources, workingScale, decision);
                        case PrefixMode.Capture:
                            return ExecuteAndCapture(context, plan, resources, workingScale, decision.Pass);
                    }
                }
                else
                {
                    // A caching toggle must not leave a previously-captured prefix buffer pinned (idempotent no-op when
                    // nothing is retained).
                    _prefixCache.Release();
                }

                return PlanExecutor.Execute(
                    plan, resources, context.Input, context.OutputScale, workingScale, context.MaxWorkingScale,
                    context.Diagnostics, context.Pool,
                    isRenderCacheEnabled: context.IsRenderCacheEnabled);
            }
            finally
            {
                graphBuilder.Abort();
            }
        }
        catch
        {
            // Ownership normally transfers to the executor. Describe, compilation, resource resolution, and prefix
            // setup can fail before that hand-off, so release the input operations here as well. Operations are
            // idempotent, making this safe when an executor path already completed its own exception sweep.
            RenderNodeOperation.DisposeAll(context.Input);
            throw;
        }
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
            context.Diagnostics, context.Pool, startPass: decision.Pass,
            isRenderCacheEnabled: context.IsRenderCacheEnabled);
    }

    // Full execution that additionally retains the capture pass's output for subsequent frames to resume from.
    private RenderNodeOperation[] ExecuteAndCapture(
        RenderNodeContext context, CompiledPlan plan, FrameResources resources, float workingScale, int capturePass)
    {
        var sink = new PrefixCaptureSink { CapturePassIndex = capturePass };
        RenderNodeOperation[] result;
        try
        {
            result = PlanExecutor.Execute(
                plan, resources, context.Input, context.OutputScale, workingScale, context.MaxWorkingScale,
                context.Diagnostics, context.Pool, startPass: 0, captureSink: sink,
                isRenderCacheEnabled: context.IsRenderCacheEnabled);
        }
        catch
        {
            // A pass after the capture pass threw once the capture pass had already shallow-copied its pooled
            // buffer into the sink; StoreCaptured never runs, so release that ref here (C7 — a thrown pass frees
            // every resource it acquired). On success the sink's ref is adopted by StoreCaptured instead.
            sink.Dispose();
            throw;
        }

        _prefixCache.StoreCaptured(sink, capturePass, plan, resources);
        return result;
    }

    protected internal override void OnServedFromCache() => _prefixCache.Release();

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _planCache.Invalidate();
        _prefixCache.Dispose();
    }
}
