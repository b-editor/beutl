using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Executes the filter operations recorded in a <see cref="FilterEffectContext"/> on a set of <see cref="EffectTargets"/>.
/// </summary>
/// <remarks>
/// <para>
/// The public constructor builds a <i>standalone</i> executor, which allocates its own intermediates from
/// the process-wide shared graphics context. That is the right answer only when the executor belongs to no
/// render — there is no caller allocation policy to honour.
/// </para>
/// <para>
/// Inside a custom effect callback there is one, so call
/// <see cref="CustomFilterEffectContext.CreateExecutor(EffectTargets, SKImageFilterBuilder)"/>
/// instead of constructing an executor: it carries
/// the running render's lease session, and so allocates through a caller-supplied
/// <see cref="IRenderTargetFactory"/> rather than drawing factory-made inputs into a shared-context buffer.
/// </para>
/// </remarks>
public sealed partial class FilterEffectExecutor : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger("FilterEffectExecutor");
    // Replaced rather than copied field by field when the working scale or execution context changes,
    // so whatever this executor starts takes the current values. Its program acquirers are always set.
    private FilterEffectExecutionSettings _settings;
    private ProgramCache<CachedSkRuntimeEffect>? _ownedProgramCache;
    private ProgramCache<ShaderGraphPipeline>? _ownedGraphProgramCache;
    private Dictionary<EffectTarget, PendingSkiaTarget>? _pendingSkiaTargets;
    // Whether an item of the pending chain reads positions in the coordinates of the bounds (IFEItem_Skia.NeedsBoundsCoordinates).
    private bool _pendingChainNeedsBoundsCoordinates;
    private bool _customEffectBoundaryMaterialized;

    /// <param name="drawableBrushMaterializer">
    /// The hook that rasterizes a <see cref="DrawableBrush.Resource"/> an effect paints with, or
    /// <see langword="null"/> when the caller applies no drawable brush. Stated rather than defaulted: left
    /// implicit, a <see cref="DrawableBrush"/> resolves to transparent instead of to its content.
    /// </param>
    public FilterEffectExecutor(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        DrawableBrushMaterializer? drawableBrushMaterializer,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        Rect? targetDomain = null)
        : this(
            targets,
            builder,
            new FilterEffectExecutionSettings(intent, purpose)
            {
                DrawableBrushMaterializer = drawableBrushMaterializer,
                OutputScale = outputScale,
                WorkingScale = workingScale,
                MaxWorkingScale = maxWorkingScale,
                TargetDomain = targetDomain,
            })
    {
    }

    /// <remarks>Without program acquirers in <paramref name="settings"/>, the executor keeps its own caches.</remarks>
    internal FilterEffectExecutor(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        FilterEffectExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);
        BufferDimensionBudget.ThrowIfUninitialized(settings.Budget, nameof(settings));

        Builder = builder;
        CurrentTargets = targets;
        _settings = settings with
        {
            OutputScale = SanitizePositiveFinite(settings.OutputScale, nameof(settings.OutputScale)),
            WorkingScale = SanitizePositiveFinite(settings.WorkingScale, nameof(settings.WorkingScale)),
            MaxWorkingScale = SanitizeCeiling(settings.MaxWorkingScale, nameof(settings.MaxWorkingScale)),
            ProgramAcquirer = settings.ProgramAcquirer ?? AcquireOwnedProgram,
            ShaderGraphProgramAcquirer = settings.ShaderGraphProgramAcquirer ?? AcquireOwnedShaderGraphProgram,
        };
    }

    internal RenderExecutionContext? ExecutionContext
    {
        get => _settings.ExecutionContext;
        set => _settings = _settings with { ExecutionContext = value };
    }

    public SKImageFilterBuilder Builder { get; }

    public EffectTargets CurrentTargets { get; }

    /// <summary>The render request's output scale <c>s_out</c>. Sanitized to positive-finite.</summary>
    public float OutputScale => _settings.OutputScale;

    /// <summary>
    /// Working density <c>w</c> for buffer allocation. Reduced in place by <see cref="Flush"/>
    /// when the dimension clamp fires. Sanitized to positive-finite.
    /// </summary>
    public float WorkingScale
    {
        get => _settings.WorkingScale;
        private set => _settings = _settings with { WorkingScale = value };
    }

    /// <summary>Working-scale ceiling forwarded into nested canvases. NaN or non-positive becomes +Inf (no ceiling).</summary>
    public float MaxWorkingScale => _settings.MaxWorkingScale;

    /// <summary>
    /// Gets the budget an allocation from this executor is held to, on both axes.
    /// </summary>
    /// <remarks>
    /// Resolved per call rather than in the constructor: an executor can outlive the moment the graphics
    /// context first answers, and until it does the engine ceiling stands in for the device's own limit.
    /// </remarks>
    public BufferDimensionBudget Budget => _settings.ResolveBudget();

    /// <summary>Gets the explicit preview or delivery classification for this execution.</summary>
    public RenderIntent Intent => _settings.Intent;

    /// <summary>Gets the explicit request purpose for this execution.</summary>
    public RenderRequestPurpose Purpose => _settings.Purpose;

    private static float SanitizeCeiling(float value, string name)
    {
        float sanitized = RenderScaleUtilities.SanitizeMaxWorkingScale(value);
        return sanitized != value ? LogAndFallback(value, name, sanitized) : sanitized;
    }

    private static float SanitizePositiveFinite(float value, string name)
    {
        if (float.IsFinite(value) && value > 0f)
            return value;
        s_logger.LogWarning("FilterEffectExecutor: {Param} ({Value}) is not positive-finite; falling back to 1.0.",
            name, value);
        return 1f;
    }

    private static float LogAndFallback(float value, string name, float fallback)
    {
        s_logger.LogWarning("FilterEffectExecutor: {Param} ({Value}) is not positive; falling back to {Fallback}.",
            name, value, fallback);
        return fallback;
    }

    private ProgramCacheLease<CachedSkRuntimeEffect> AcquireOwnedProgram(
        EffectTarget target,
        string source)
    {
        ProgramCache<CachedSkRuntimeEffect> cache =
            _ownedProgramCache ??= SkRuntimeEffectProgramCache.Create();
        RenderTarget destination = target.RenderTarget
            ?? throw new InvalidOperationException(
                "An effect-item shader program requires a materialized execution destination.");
        return SkRuntimeEffectProgramCache.AcquireForDestination(
            cache,
            destination,
            source);
    }

    private ProgramCacheLease<ShaderGraphPipeline> AcquireOwnedShaderGraphProgram(VulkanContext context, ShaderPassDescription pass,
        ShaderProgramIdentity identity)
    {
        ProgramCache<ShaderGraphPipeline> cache = _ownedGraphProgramCache ??= ShaderGraphPipeline.CreateCache();
        cache.SynchronizeContext(cache, context);
        return ShaderGraphPipeline.Acquire(cache, new ProgramCacheContextKey(cache, context, "shader-graph", "v1"), context, pass, identity);
    }

    public void Dispose()
    {
        _ownedProgramCache?.Dispose();
        _ownedGraphProgramCache?.Dispose();
    }

    private void BeginSkiaChain()
    {
        if (_pendingSkiaTargets is not null) return;
        _pendingSkiaTargets = new Dictionary<EffectTarget, PendingSkiaTarget>();
        foreach (EffectTarget target in CurrentTargets)
        {
            Rect physicalBounds = target.RasterBounds.Translate(
                target.OriginalBounds.Position - target.Bounds.Position);
            // OriginalBounds cannot serve as the anchor frame: a stage the fallback executor allocated
            // itself begins a chain with OriginalBounds == Bounds, which anchors the chain at zero.
            _pendingSkiaTargets.Add(
                target,
                new PendingSkiaTarget(
                    target.Bounds,
                    physicalBounds,
                    new Rect(default, target.Bounds.Size)));
        }
    }

    public void Apply(FilterEffectContext context) => Apply(context, null);

    /// <param name="requiredOutput">
    /// The part of the output the caller keeps, or <see langword="null"/> for all of it. A shader graph that only Skia
    /// filters with a sampling footprint follow then produces only what they read of it. It holds for this call alone:
    /// nested executors and continuations produce all of their output.
    /// </param>
    internal void Apply(FilterEffectContext context, Rect? requiredOutput)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (CurrentTargets.Count == 0) return;
        context.PrepareStandaloneResourcesForExecution();

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        for (int index = 0; index < items.Count; index++)
        {
            if (index == context._items.Count && context._renderTimeItems.Count > 0)
            {
                Flush(false);
                if (CurrentTargets.Count == 0) return;
            }
            IFEItem item = items[index];
            switch (item)
            {
                case IFEItem_Skia skia:
                    AccumulateSkiaItem(skia);
                    break;
                case IFEItem_Custom custom:
                    // A custom effect runs author code against materialized targets, so the pending
                    // chain has to be forced out before it can see them.
                    Flush();
                    if (CurrentTargets.Count == 0) return;
                    bool controlsContinuation = custom.Options.HasFlag(CustomFilterEffectOptions.ControlsContinuation);
                    RunCustomEffectItem(custom, controlsContinuation ? items.Skip(index + 1).ToArray() : null);
                    if (controlsContinuation) return;
                    break;
                case FEItem_ShaderGraph graph:
                    Flush(false);
                    if (CurrentTargets.Count == 0) return;
                    FilterEffectStageFallbackExecutor.ApplyShaderGraph(CurrentTargets, graph.Description, _settings,
                        requiredOutput is { } output
                        && EffectItemSamplingSupport.TryResolveStageOutput(items, index, output, out Rect stageOutput)
                            ? stageOutput
                            : null);
                    break;
                case FEItem_Shader shader:
                    Flush(false);
                    if (CurrentTargets.Count == 0) return;
                    FilterEffectStageFallbackExecutor.ApplyShader(CurrentTargets, shader.Description, _settings);
                    break;
                case FEItem_Geometry geometry:
                    Flush(false);
                    if (CurrentTargets.Count == 0) return;
                    FilterEffectStageFallbackExecutor.ApplyGeometry(CurrentTargets, geometry.Description, _settings);
                    break;
            }
        }

    }

    /// <summary>Adds one Skia item to the pending chain and maps every target's bounds through it.</summary>
    private void AccumulateSkiaItem(IFEItem_Skia skia)
    {
        // A deferred-bound Skia item's origin depends on input bounds a preceding custom
        // effect may only re-target at execution time, so this activation resolves its own
        // matrix from the combined execution-time target bounds before the filter is built.
        // Both the built filter and every bounds mapping below then use that one matrix.
        IFEItem_Skia effectiveItem = skia is IFEItem_DeferredBounds deferred
            ? deferred.ResolveForActivation(CurrentTargets.CalculateBounds())
            : skia;

        BeginSkiaChain();
        effectiveItem.Accepts(this, Builder);
        _pendingChainNeedsBoundsCoordinates |= effectiveItem.NeedsBoundsCoordinates;

        foreach (EffectTarget t in CurrentTargets)
        {
            PendingSkiaTarget pending = _pendingSkiaTargets![t];
            // PhysicalBounds shares OriginalBounds' frame, and AnchorFrame has its own. An item that reads positions in
            // the coordinates of the bounds maps each in those coordinates: these offsets take a frame there.
            Vector original = effectiveItem.NeedsBoundsCoordinates
                ? t.Bounds.Position - t.OriginalBounds.Position
                : default;
            Vector anchor = effectiveItem.NeedsBoundsCoordinates
                ? t.Bounds.Position - pending.AnchorFrame.Position
                : default;
            pending.PhysicalBounds = TransformBoundsFrom(effectiveItem, pending.PhysicalBounds, original);
            pending.AnchorFrame = TransformBoundsFrom(effectiveItem, pending.AnchorFrame, anchor);
            t.Bounds = effectiveItem.TransformBounds(t.Bounds);
            t.OriginalBounds = TransformBoundsFrom(effectiveItem, t.OriginalBounds, original);
            // The chain's execution frame is anchored at InputBounds.Position, which
            // must stay equal to the displacement this item's accumulated mapping
            // gives the chain-start Bounds.Position. A translation-invariant item
            // preserves that displacement, so this is a no-op there; a matrix item
            // moves Bounds relative to the anchor frame and has to re-anchor with it.
            pending.InputBounds = new Rect(
                t.Bounds.Position - pending.AnchorFrame.Position,
                pending.InputBounds.Size);
        }
    }

    // Maps bounds held in a frame that offset takes into the coordinates of the bounds.
    private static Rect TransformBoundsFrom(IFEItem item, Rect bounds, Vector offset)
        => offset == default
            ? item.TransformBounds(bounds)
            : item.TransformBounds(bounds.Translate(offset)).Translate(-offset);

    /// <summary>Hands the materialized targets to author code, and re-anchors what it left behind.</summary>
    private void RunCustomEffectItem(IFEItem_Custom custom, IFEItem[]? continuation)
    {
        _customEffectBoundaryMaterialized = true;

        RenderExecutionContext? execution = ExecutionContext?.CreateCallbackScope(WorkingScale, custom);
        var customContext = new CustomFilterEffectContext(CurrentTargets, _settings with { ExecutionContext = execution });
        if (continuation is not null)
            customContext.Continuation = new FilterEffectContinuation(customContext, continuation);
        if (execution is { CanAccessTarget: true })
            customContext.TargetAccess = new CompositionTargetAccess(execution);
        using RenderExecutionCallbackGuard.Scope guard = ExecutionContext is null
            ? default : RenderExecutionCallbackGuard.Enter();
        try { custom.Accepts(customContext); }
        finally
        {
            customContext.EndCallback();
            execution?.End();
        }

        foreach (EffectTarget t in CurrentTargets)
        {
            t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
        }
    }

    // Skia factories must not re-enter their executor.
    public SKImageFilter? Activate(FilterEffectContext context)
    {
        Flush(false);

        using EffectTargets cloned = CurrentTargets.Clone();
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            cloned,
            builder,
            _settings.WithDeviceGridOffsetFrom(cloned));

        executor.Apply(context);
        executor.Flush(false);

        SKImageFilter? filter = builder.GetFilter();
        if (filter != null) return filter;

        foreach (EffectTarget t in executor.CurrentTargets)
        {
            if (t.RenderTarget == null) continue;

            SKSurface innerSurface = t.RenderTarget.Value;
            using var snapshot = SurfaceSnapshot.Take(innerSurface);
            SKImage skImage = snapshot.Image;

            Rect rasterBounds = t.RasterBounds;
            SKImageFilter image = SKImageFilter.CreateImage(
                skImage,
                new SKRect(0, 0, skImage.Width, skImage.Height),
                rasterBounds.ToSKRect(),
                SKSamplingOptions.Default);

            filter = filter == null ? image : SKImageFilter.CreateCompose(filter, image);
        }

        return filter;
    }

    private sealed class PendingSkiaTarget(
        Rect inputBounds,
        Rect physicalBounds,
        Rect anchorFrame)
    {
        public Rect InputBounds { get; set; } = inputBounds;

        /// <summary>Where the coordinates of the bounds put the input's origin when the chain began.</summary>
        public Point ChainOrigin { get; } = inputBounds.Position;

        public Rect PhysicalBounds { get; set; } = physicalBounds;

        /// <summary>
        /// The chain-start frame, mapped alongside Bounds, whose position anchors the flush frame. It keeps the
        /// chain-start Bounds size because a bounds map displaces two rects equally only while their sizes agree.
        /// </summary>
        public Rect AnchorFrame { get; set; } = anchorFrame;
    }
}
