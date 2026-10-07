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
/// <see cref="CustomFilterEffectContext.CreateExecutor"/> instead of constructing an executor: it carries
/// the running render's lease session, and so allocates through a caller-supplied
/// <see cref="IRenderTargetFactory"/> rather than drawing factory-made inputs into a shared-context buffer.
/// </para>
/// </remarks>
public sealed partial class FilterEffectExecutor : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger("FilterEffectExecutor");
    private readonly SkRuntimeEffectProgramAcquirer? _injectedProgramAcquirer;
    private readonly Vector? _deviceGridOffset;
    private readonly DrawableBrushMaterializer? _drawableBrushMaterializer;
    private readonly bool _useExecutorManagedCanvas;
    private readonly RenderTargetLeaseSession? _renderTargetLeaseSession;
    private readonly BufferDimensionBudget? _budget;
    private readonly Rect? _targetDomain;
    private ProgramCache<CachedSkRuntimeEffect>? _ownedProgramCache;
    private Dictionary<EffectTarget, PendingSkiaTarget>? _pendingSkiaTargets;
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
            intent,
            purpose,
            outputScale,
            workingScale,
            maxWorkingScale,
            acquireProgram: null,
            deviceGridOffset: null,
            ownsProgramCache: true,
            drawableBrushMaterializer,
            useExecutorManagedCanvas: false,
            renderTargetLeaseSession: null,
            budget: null,
            targetDomain)
    {
    }

    internal FilterEffectExecutor(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        Vector deviceGridOffset,
        DrawableBrushMaterializer? drawableBrushMaterializer = null,
        bool useExecutorManagedCanvas = false,
        RenderTargetLeaseSession? renderTargetLeaseSession = null,
        BufferDimensionBudget? budget = null,
        Rect? targetDomain = null)
        : this(
            targets,
            builder,
            intent,
            purpose,
            outputScale,
            workingScale,
            maxWorkingScale,
            acquireProgram: null,
            deviceGridOffset,
            ownsProgramCache: true,
            drawableBrushMaterializer,
            useExecutorManagedCanvas,
            renderTargetLeaseSession,
            budget,
            targetDomain)
    {
    }

    internal FilterEffectExecutor(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        Vector deviceGridOffset,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        DrawableBrushMaterializer? drawableBrushMaterializer = null,
        bool useExecutorManagedCanvas = false,
        RenderTargetLeaseSession? renderTargetLeaseSession = null,
        BufferDimensionBudget? budget = null,
        Rect? targetDomain = null)
        : this(
            targets,
            builder,
            intent,
            purpose,
            outputScale,
            workingScale,
            maxWorkingScale,
            acquireProgram ?? throw new ArgumentNullException(nameof(acquireProgram)),
            deviceGridOffset,
            ownsProgramCache: false,
            drawableBrushMaterializer,
            useExecutorManagedCanvas,
            renderTargetLeaseSession,
            budget,
            targetDomain)
    {
    }

    private FilterEffectExecutor(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        SkRuntimeEffectProgramAcquirer? acquireProgram,
        Vector? deviceGridOffset,
        bool ownsProgramCache,
        DrawableBrushMaterializer? drawableBrushMaterializer,
        bool useExecutorManagedCanvas,
        RenderTargetLeaseSession? renderTargetLeaseSession,
        BufferDimensionBudget? budget,
        Rect? targetDomain)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(builder);
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");
        BufferDimensionBudget.ThrowIfUninitialized(budget, nameof(budget));

        _budget = budget;
        Builder = builder;
        CurrentTargets = targets;
        OutputScale = SanitizePositiveFinite(outputScale, nameof(outputScale));
        WorkingScale = SanitizePositiveFinite(workingScale, nameof(workingScale));
        MaxWorkingScale = SanitizeCeiling(maxWorkingScale, nameof(maxWorkingScale));
        Intent = intent;
        Purpose = purpose;
        _deviceGridOffset = deviceGridOffset;
        _drawableBrushMaterializer = drawableBrushMaterializer;
        _useExecutorManagedCanvas = useExecutorManagedCanvas;
        _renderTargetLeaseSession = renderTargetLeaseSession;
        _targetDomain = targetDomain;
        if (!ownsProgramCache)
        {
            _injectedProgramAcquirer = acquireProgram
                ?? throw new ArgumentNullException(nameof(acquireProgram));
        }
    }

    public SKImageFilterBuilder Builder { get; }

    public EffectTargets CurrentTargets { get; }

    /// <summary>The render request's output scale <c>s_out</c>. Sanitized to positive-finite.</summary>
    public float OutputScale { get; }

    /// <summary>
    /// Working density <c>w</c> for buffer allocation. Reduced in place by <see cref="Flush"/>
    /// when the dimension clamp fires. Sanitized to positive-finite.
    /// </summary>
    public float WorkingScale { get; private set; }

    /// <summary>Working-scale ceiling forwarded into nested canvases. NaN or non-positive becomes +Inf (no ceiling).</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// Gets the budget an allocation from this executor is held to, on both axes.
    /// </summary>
    /// <remarks>
    /// Resolved per call rather than in the constructor: an executor can outlive the moment the graphics
    /// context first answers, and until it does the engine ceiling stands in for the device's own limit.
    /// </remarks>
    public BufferDimensionBudget Budget
        => _budget ?? BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation);

    /// <summary>Gets the explicit preview or delivery classification for this execution.</summary>
    public RenderIntent Intent { get; }

    /// <summary>Gets the explicit request purpose for this execution.</summary>
    public RenderRequestPurpose Purpose { get; }

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
                "A effectItem shader program requires a materialized execution destination.");
        return SkRuntimeEffectProgramCache.AcquireForDestination(
            cache,
            destination,
            source);
    }

    private SkRuntimeEffectProgramAcquirer GetProgramAcquirer()
        => _injectedProgramAcquirer ?? AcquireOwnedProgram;

    public void Dispose()
    {
        _ownedProgramCache?.Dispose();
    }

    /// <summary>
    /// Makes sure every current target has chain bookkeeping, keeping what an in-progress chain accumulated.
    /// </summary>
    /// <remarks>
    /// A Skia item runs author code that may re-enter <see cref="Activate"/> or <see cref="Flush"/>, both of
    /// which drop this map and can replace the targets it was keyed by. Entries are therefore added rather
    /// than the map rebuilt, so calling this again after author code has run restores a dropped map and covers
    /// a target that appeared, without resetting a chain that survived.
    /// </remarks>
    private void BeginSkiaChain()
    {
        _pendingSkiaTargets ??= new Dictionary<EffectTarget, PendingSkiaTarget>();
        foreach (EffectTarget target in CurrentTargets)
        {
            if (_pendingSkiaTargets.ContainsKey(target))
                continue;

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

    // 最小単位である'IFEItem'の数がわからないので 'count'は'nullable'
    public void Apply(FilterEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (CurrentTargets.Count == 0) return;
        context.PrepareStandaloneResourcesForExecution();

        foreach (IFEItem item in context._items)
        {
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
                    RunCustomEffectItem(custom);
                    break;
                case FEItem_Shader shader:
                    Flush(false);
                    if (CurrentTargets.Count == 0) return;
                    FilterEffectStageFallbackExecutor.ApplyShader(
                        CurrentTargets,
                        shader.Description,
                        OutputScale,
                        WorkingScale,
                        MaxWorkingScale,
                        Intent,
                        Purpose,
                        GetProgramAcquirer(),
                        _renderTargetLeaseSession,
                        Budget);
                    break;
                case FEItem_Geometry geometry:
                    Flush(false);
                    if (CurrentTargets.Count == 0) return;
                    FilterEffectStageFallbackExecutor.ApplyGeometry(
                        CurrentTargets,
                        geometry.Description,
                        OutputScale,
                        WorkingScale,
                        MaxWorkingScale,
                        Intent,
                        Purpose,
                        _renderTargetLeaseSession,
                        Budget);
                    break;
            }
        }

        ApplyRenderTimeItems(context);
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
        // Author code just ran and may have gone through Activate() or Flush(), either of
        // which drops the bookkeeping this loop is about to read.
        BeginSkiaChain();

        foreach (EffectTarget t in CurrentTargets)
        {
            PendingSkiaTarget pending = _pendingSkiaTargets![t];
            pending.PhysicalBounds = effectiveItem.TransformBounds(pending.PhysicalBounds);
            pending.AnchorFrame = effectiveItem.TransformBounds(pending.AnchorFrame);
            t.Bounds = effectiveItem.TransformBounds(t.Bounds);
            t.OriginalBounds = effectiveItem.TransformBounds(t.OriginalBounds);
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

    /// <summary>Hands the materialized targets to author code, and re-anchors what it left behind.</summary>
    private void RunCustomEffectItem(IFEItem_Custom custom)
    {
        _customEffectBoundaryMaterialized = true;

        var customContext = new CustomFilterEffectContext(
            CurrentTargets,
            Intent,
            Purpose,
            OutputScale,
            WorkingScale,
            MaxWorkingScale,
            _deviceGridOffset,
            _drawableBrushMaterializer,
            _useExecutorManagedCanvas,
            _renderTargetLeaseSession,
            _budget,
            _targetDomain);
        custom.Accepts(customContext);

        foreach (EffectTarget t in CurrentTargets)
        {
            t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
        }
    }

    /// <summary>Re-enters with the items that could only be recorded once the render was under way.</summary>
    private void ApplyRenderTimeItems(FilterEffectContext context)
    {
        if (context._renderTimeItems.Count <= 0) return;

        Flush(false);
        if (CurrentTargets.Count == 0) return;
        using var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds(), OutputScale, WorkingScale);

        foreach (IFEItem item in context._renderTimeItems)
        {
            ctx._items.Add(item);
        }

        Apply(ctx);
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        // A no-op Flush still drops the pending-Skia bookkeeping, which the caller's own in-progress chain
        // still needs when it authored this call from a Skia factory.
        Dictionary<EffectTarget, PendingSkiaTarget>? pendingSkiaTargets =
            Builder.HasFilter() ? null : _pendingSkiaTargets;
        Flush(false);
        _pendingSkiaTargets = pendingSkiaTargets;

        using EffectTargets cloned = CurrentTargets.Clone();
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            cloned,
            builder,
            Intent,
            Purpose,
            OutputScale,
            WorkingScale,
            MaxWorkingScale,
            _deviceGridOffset
                ?? (cloned.Count > 0 ? cloned[0].DeviceGridOffset : default),
            GetProgramAcquirer(),
            _drawableBrushMaterializer,
            _useExecutorManagedCanvas,
            _renderTargetLeaseSession,
            _budget,
            _targetDomain);

        executor.Apply(context);
        executor.Flush(false);

        SKImageFilter? filter = builder.GetFilter();
        if (filter != null) return filter;

        foreach (EffectTarget t in executor.CurrentTargets)
        {
            if (t.RenderTarget == null) continue;

            SKSurface innerSurface = t.RenderTarget.Value;
            using SKImage skImage = innerSurface.Snapshot();

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

        public Rect PhysicalBounds { get; set; } = physicalBounds;

        /// <summary>
        /// The chain-start frame: the origin at the chain-start <see cref="EffectTarget.Bounds"/> size,
        /// mapped by every item alongside them. Subtracting its position from the mapped Bounds position
        /// leaves the chain-start Bounds position under the accumulated linear part, which is the anchor
        /// the flush frame needs. The matching size is what makes that subtraction cancel: a bounds map
        /// displaces two rects by the same amount only while their sizes agree.
        /// </summary>
        public Rect AnchorFrame { get; set; } = anchorFrame;
    }

    private ImmediateCanvas CreateExecutionCanvas(
        RenderTarget target,
        float density,
        Size logicalSize)
        => ImmediateCanvas.CreateCustomEffectCanvas(
            target,
            density,
            MaxWorkingScale,
            logicalSize,
            Intent,
            _useExecutorManagedCanvas,
            _drawableBrushMaterializer);
}
