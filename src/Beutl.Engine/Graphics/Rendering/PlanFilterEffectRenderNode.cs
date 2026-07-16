using System.Runtime.ExceptionServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// The standard declarative filter-effect render node. It owns compilation, parameter rebind, ROI, pooling, and
/// plan/prefix caches while exposing narrow policy hooks to subclasses.
/// </summary>
public class PlanFilterEffectRenderNode(FilterEffect.Resource filterEffect) : FilterEffectRenderNode(filterEffect)
{
    private static readonly ILogger s_logger = Log.CreateLogger<PlanFilterEffectRenderNode>();

    // Keyed on the graphics-context identity when none is resolved yet (the pool-less / no-GPU path), so the cache
    // still functions and a later real context is treated as a change.
    private static readonly object s_noContext = new();

    [ThreadStatic]
    private static Action? s_beforeStoreCapturedForTest;

    [ThreadStatic]
    private static Action? s_beforeDisabledPrefixReleaseForTest;

    internal static void SetBeforeStoreCapturedForTest(Action? callback)
        => s_beforeStoreCapturedForTest = callback;

    internal static void SetBeforeDisabledPrefixReleaseForTest(Action? callback)
        => s_beforeDisabledPrefixReleaseForTest = callback;

    private readonly RuntimeState _frameState = new();
    private readonly RuntimeState _auxiliaryState = new();
    private readonly EffectPrefixCache _prefixCache = new();

    public sealed override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        RuntimeState runtimeState = context.IsAuxiliaryPull ? _auxiliaryState : _frameState;
        if (FilterEffect == null || !FilterEffect.Value.Resource.IsEnabled)
        {
            // A disabled/removed effect bypasses execution, but a prefix retained from an earlier enabled frame would
            // stay pinned outside every pool budget until node dispose. Release it (idempotent); a later re-enable
            // re-warms from scratch.
            try
            {
                if (!context.IsAuxiliaryPull)
                {
                    s_beforeDisabledPrefixReleaseForTest?.Invoke();
                    _prefixCache.Release();
                }

                runtimeState.NestedPlanCache.NotifyServedFromCache();
            }
            catch
            {
                // The disabled path returns before the main exception sweep. If releasing a retained native target
                // fails, the bypassed input operations must still be consumed exactly like every other Process throw.
                RenderNodeOperation.DisposeAll(context.Input);
                throw;
            }

            return context.Input;
        }

        bool inputCleanupCompleted = false;
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

            float workingScale = ResolveWorkingScale(context, inputScales);
            if (!float.IsFinite(workingScale) || workingScale <= 0f || workingScale > context.MaxWorkingScale)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}.{nameof(ResolveWorkingScale)} returned {workingScale}; the value must be "
                    + $"positive, finite, and no greater than {context.MaxWorkingScale}.");
            }

            Rect bounds = context.CalculateBounds();

            FilterEffect.Resource resource = FilterEffect.Value.Resource;
            // A Describe that registers a native sampler/child shader and then throws would strand it (ownership only
            // transfers to the graph at Build); the engine aborts the still-open builder in the finally path.
            var graphBuilder = new EffectGraphBuilder(
                bounds, context.OutputScale, workingScale, context.RenderIntent,
                context.MaxWorkingScale, runtimeState.NestedPlanCache, context.PullPurpose);
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
                if (runtimeState.PlanCache.TryGet(key, contextId, out CompiledPlan cached))
                {
                    plan = ParameterBlock.Extract(graph).RebindOnto(cached);
                }
                else
                {
                    plan = EffectGraphCompiler.Compile(graph, context.Diagnostics);
                    runtimeState.PlanCache.Store(key, contextId, plan);
                }

                FrameResources resources = EffectGraphCompiler.ResolveResources(plan, context.RequestedBounds, workingScale);

                // Pass-prefix output caching (C10): reuse a stable leading run of passes so a heavy static prefix (a blur,
                // a stroke) is not re-executed every frame merely because the tail is animated. Only engaged on the pooled
                // render path with render caching enabled — a caller that disabled render caching (a delivery render) must
                // not have frames served from a retained prefix; the pool-less golden/frozen harnesses render once and never
                // reach the engagement threshold.
                if (context.IsAuxiliaryPull)
                {
                    // Hit-test/bounds pulls commonly request full bounds while the frame render uses a cropped ROI.
                    // Execute the auxiliary result without consulting or mutating the retained frame-prefix slice.
                    return PlanExecutor.Execute(
                        plan, resources, context.Input, context.OutputScale, workingScale, context.MaxWorkingScale,
                        context.Diagnostics, context.Pool,
                        isRenderCacheEnabled: context.IsRenderCacheEnabled,
                        pullPurpose: context.PullPurpose,
                        renderIntent: context.RenderIntent,
                        renderTargetFactory: context.RenderTargetFactory);
                }

                if (context.Pool != null && context.IsRenderCacheEnabled)
                {
                    bool inputSubtreeStable = context.InputSubtreeStableOverride
                        ?? RenderNodeCacheHelper.CanCacheRecursiveChildrenOnly(this);
                    PrefixDecision decision = _prefixCache.Prepare(
                        resource, plan, key, contextId, workingScale, context.OutputScale, context.MaxWorkingScale,
                        context.RenderIntent, context.Input, inputSubtreeStable,
                        resources);

                    switch (decision.Mode)
                    {
                        case PrefixMode.Resume:
                            return ExecuteResumed(
                                context, plan, resources, workingScale, decision, ref inputCleanupCompleted);
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
                    isRenderCacheEnabled: context.IsRenderCacheEnabled,
                    pullPurpose: context.PullPurpose,
                    renderIntent: context.RenderIntent,
                    renderTargetFactory: context.RenderTargetFactory);
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
            if (!inputCleanupCompleted)
                RenderNodeOperation.DisposeAll(context.Input);
            throw;
        }
    }

    /// <summary>
    /// Resolves the working density used by the standard plan pipeline. Overrides must return a positive finite value
    /// no greater than <see cref="RenderNodeContext.MaxWorkingScale"/>.
    /// </summary>
    protected virtual float ResolveWorkingScale(
        RenderNodeContext context,
        ReadOnlySpan<EffectiveScale> inputScales)
        => RenderNodeContext.ResolveWorkingScale(
            inputScales, context.OutputScale, context.MaxWorkingScale);

    // Skips the retained prefix's passes: the cached buffer seeds the pass after it, so passes 0..k neither draw nor
    // allocate. The fresh input ops are discarded (the stable prefix already encapsulates them), counting one hit.
    private RenderNodeOperation[] ExecuteResumed(
        RenderNodeContext context, CompiledPlan plan, FrameResources resources, float workingScale,
        PrefixDecision decision, ref bool inputCleanupCompleted)
    {
        Exception? cleanupFailure = DisposeResumedInputs(context.Input);
        inputCleanupCompleted = true;
        if (cleanupFailure != null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();

        if (context.Diagnostics != null)
            context.Diagnostics.PrefixCacheHits++;

        RenderNodeOperation seed = RenderNodeOperation.CreateFromRenderTarget(
            decision.SeedBounds, decision.SeedBounds.Position, decision.SeedTarget!.ShallowCopy(), decision.SeedScale);
        return PlanExecutor.Execute(
            plan, resources, [seed], context.OutputScale, workingScale, context.MaxWorkingScale,
            context.Diagnostics, context.Pool, startPass: decision.Pass,
            isRenderCacheEnabled: context.IsRenderCacheEnabled,
            pullPurpose: context.PullPurpose,
            renderIntent: context.RenderIntent,
            renderTargetFactory: context.RenderTargetFactory);
    }

    internal static Exception? DisposeResumedInputs(ReadOnlySpan<RenderNodeOperation> input)
    {
        Exception? cleanupFailure = null;
        RenderNodeOperation.DisposeAll(input, ref cleanupFailure);
        return cleanupFailure;
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
                isRenderCacheEnabled: context.IsRenderCacheEnabled,
                pullPurpose: context.PullPurpose,
                renderIntent: context.RenderIntent,
                renderTargetFactory: context.RenderTargetFactory);
        }
        catch
        {
            // A pass after the capture pass threw once the capture pass had already shallow-copied its pooled
            // buffer into the sink; StoreCaptured never runs, so release that ref here (C7 — a thrown pass frees
            // every resource it acquired). On success the sink's ref is adopted by StoreCaptured instead.
            try
            {
                sink.Dispose();
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "A captured prefix target failed to dispose after plan execution failed");
            }

            throw;
        }

        try
        {
            s_beforeStoreCapturedForTest?.Invoke();
            _prefixCache.StoreCaptured(
                sink, capturePass, plan, resources,
                context.Pool ?? throw new InvalidOperationException("Prefix capture requires a render-target pool."));
        }
        catch
        {
            // The executor has returned successfully, but neither ownership set has reached the caller yet. Sweep the
            // final operations, the not-yet-adopted shallow copy, and a possibly partially-adopted cache entry without
            // allowing a cleanup failure to replace the StoreCaptured exception.
            RenderNodeOperation.DisposeAll(result);
            try
            {
                sink.Dispose();
            }
            catch
            {
            }

            try
            {
                _prefixCache.Release();
            }
            catch
            {
            }

            throw;
        }

        return result;
    }

    protected internal override void OnServedFromCache()
    {
        Exception? cleanupFailure = null;
        try
        {
            _prefixCache.Release();
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }

        try
        {
            _frameState.NestedPlanCache.NotifyServedFromCache();
        }
        catch (Exception ex)
        {
            cleanupFailure ??= ex;
        }

        try
        {
            _auxiliaryState.NestedPlanCache.NotifyServedFromCache();
        }
        catch (Exception ex)
        {
            cleanupFailure ??= ex;
        }

        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    protected override void OnDispose(bool disposing)
    {
        Exception? cleanupFailure = null;
        try
        {
            base.OnDispose(disposing);
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }

        try
        {
            _frameState.Dispose();
        }
        catch (Exception ex)
        {
            cleanupFailure ??= ex;
        }

        try
        {
            _auxiliaryState.Dispose();
        }
        catch (Exception ex)
        {
            cleanupFailure ??= ex;
        }

        try
        {
            _prefixCache.Dispose();
        }
        catch (Exception ex)
        {
            cleanupFailure ??= ex;
        }

        if (disposing && cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private sealed class RuntimeState : IDisposable
    {
        public PlanCache PlanCache { get; } = new();

        public NestedGraphPlanCache NestedPlanCache { get; } = new();

        public void Dispose()
        {
            PlanCache.Invalidate();
            NestedPlanCache.Dispose();
        }
    }
}
