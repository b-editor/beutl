using System.ComponentModel;
using Beutl.Collections.Pooled;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Microsoft.Extensions.ObjectPool;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class FilterEffectContext : IDisposable
{
    internal readonly PooledList<IFEItem> _items;
    internal readonly PooledList<IFEItem> _renderTimeItems;
    private readonly FilterEffectResourceState _resourceState;
    private readonly float _resolvedWorkingScale;
    private readonly Lazy<float>? _workingScaleResolver;
    private readonly bool _hasResolvedWorkingScale;
    private bool _disposed;

    internal static readonly ObjectPool<float[]> s_colorMatPool;

    static FilterEffectContext()
    {
        s_colorMatPool = new DefaultObjectPool<float[]>(new ArrayPooledObjectPolicy<float>(ColorMatrixShader.SkiaColorMatrixLength));
    }

    /// <summary>Holds a Skia colour-matrix buffer rented from <see cref="s_colorMatPool"/> and returns it on dispose.</summary>
    private readonly struct PooledColorMatrix : IDisposable
    {
        private readonly float[] _array;

        private PooledColorMatrix(float[] array)
        {
            _array = array;
        }

        public static PooledColorMatrix Rent(out float[] array)
        {
            array = s_colorMatPool.Get();
            return new PooledColorMatrix(array);
        }

        public void Dispose() => s_colorMatPool.Return(_array);
    }

    public FilterEffectContext(Rect bounds, float outputScale = 1f, float workingScale = 1f)
        : this(
            bounds,
            outputScale,
            workingScale,
            workingScaleResolver: null,
            hasResolvedWorkingScale: true,
            new FilterEffectResourceState(renderContext: null))
    {
    }

    internal FilterEffectContext(
        Rect bounds,
        float outputScale,
        float workingScale,
        RenderNodeContext renderContext,
        bool hasResolvedWorkingScale = true)
        : this(
            bounds,
            outputScale,
            workingScale,
            workingScaleResolver: null,
            hasResolvedWorkingScale,
            new FilterEffectResourceState(renderContext))
    {
    }

    internal FilterEffectContext(
        Rect bounds,
        float outputScale,
        Func<float> resolveWorkingScale,
        RenderNodeContext renderContext)
        : this(
            bounds,
            outputScale,
            resolvedWorkingScale: default,
            workingScaleResolver: new Lazy<float>(
                resolveWorkingScale ?? throw new ArgumentNullException(nameof(resolveWorkingScale))),
            hasResolvedWorkingScale: true,
            new FilterEffectResourceState(renderContext))
    {
    }

    private FilterEffectContext(
        Rect bounds,
        float outputScale,
        float resolvedWorkingScale,
        Lazy<float>? workingScaleResolver,
        bool hasResolvedWorkingScale,
        FilterEffectResourceState resourceState)
    {
        _bounds = OriginalBounds = bounds;
        OutputScale = outputScale;
        _resolvedWorkingScale = resolvedWorkingScale;
        _workingScaleResolver = workingScaleResolver;
        _hasResolvedWorkingScale = hasResolvedWorkingScale;
        _resourceState = resourceState;
        _renderTimeItems = [];
        _items = [];
    }

    private FilterEffectContext(FilterEffectContext obj)
    {
        OriginalBounds = obj.OriginalBounds;
        _bounds = obj._bounds;
        OutputScale = obj.OutputScale;
        _resolvedWorkingScale = obj._resolvedWorkingScale;
        _workingScaleResolver = obj._workingScaleResolver;
        _hasResolvedWorkingScale = obj._hasResolvedWorkingScale;
        _resourceState = obj._resourceState.AddReference();
        _renderTimeItems = new PooledList<IFEItem>(obj._renderTimeItems);
        _items = new PooledList<IFEItem>(obj._items);
    }

    private FilterEffectContext(
        FilterEffectContext obj,
        Rect bounds)
    {
        OriginalBounds = _bounds = bounds;
        OutputScale = obj.OutputScale;
        _resolvedWorkingScale = obj._resolvedWorkingScale;
        _workingScaleResolver = obj._workingScaleResolver;
        _hasResolvedWorkingScale = obj._hasResolvedWorkingScale;
        _resourceState = obj._resourceState.AddReference();
        _renderTimeItems = [];
        _items = [];
    }

    private Rect _bounds;

    internal Rect Bounds => _bounds;

    public Rect OriginalBounds { get; }

    /// <summary>
    /// The output scale <c>s_out</c> for this render request; never a ceiling on working scale.
    /// </summary>
    public float OutputScale { get; }

    /// <summary>
    /// The nominal effect-input density <c>w</c> from which authored operations negotiate their buffers using the
    /// canonical near-edge/far-edge composition-device footprint.
    /// Resolved per-effect via <see cref="Beutl.Graphics.Rendering.RenderScaleUtilities.ResolveWorkingScale"/>.
    /// </summary>
    /// <remarks>An expanding operation may run below this value after its own per-buffer dimension clamp.</remarks>
    /// <exception cref="InvalidOperationException">
    /// The effect is being authored against unresolved or branch-dependent input metadata, so one final working
    /// scale is not available. Use <see cref="TryGetWorkingScale"/> to probe availability and defer device-pixel
    /// math to execution-time shader, geometry, or custom-effect callbacks.
    /// </exception>
    public float WorkingScale
        => TryGetWorkingScale(out float workingScale)
            ? workingScale
            : throw new InvalidOperationException(
                "The filter-effect working scale is unavailable because its input metadata is unresolved or "
                + "different branches may lower at different densities. Use TryGetWorkingScale during ApplyTo "
                + "and perform device-pixel math in an "
                + "execution-time shader, geometry, or custom-effect callback.");

    /// <summary>Tries to get the nominal effect-input working density available while authoring this effect.</summary>
    /// <param name="workingScale">
    /// Receives the positive finite working density, or <see langword="default"/> when input metadata is unresolved
    /// or multiple input branches may lower at different densities.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when one concrete effect-input density is available;
    /// otherwise <see langword="false"/> because the inputs are unresolved or branch-dependent.
    /// </returns>
    /// <remarks>See the remarks on <see cref="WorkingScale"/>.</remarks>
    public bool TryGetWorkingScale(out float workingScale)
    {
        workingScale = _hasResolvedWorkingScale
            ? _workingScaleResolver?.Value ?? _resolvedWorkingScale
            : default;
        return _hasResolvedWorkingScale;
    }

    public FilterEffectContext Clone()
    {
        ThrowIfDisposed();
        return new FilterEffectContext(this);
    }

    public FilterEffectContext CreateChildContext()
    {
        ThrowIfDisposed();
        return new FilterEffectContext(this, _bounds);
    }

    private void AddItem(IFEItem item)
    {
        ThrowIfDisposed();
        if (!_bounds.IsInvalid)
        {
            _items.Add(item);
        }
        else
        {
            _renderTimeItems.Add(item);
        }
    }

    /// <summary>Appends one shader stage to this filter-effect stream.</summary>
    /// <param name="description">
    /// The non-null immutable stage contract. Every declared resource must belong to this context's family.
    /// </param>
    public void Shader(ShaderDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _resourceState.ValidateResources(
            description.Resources,
            static binding => binding.Resource,
            nameof(description));
        AppendDescription(new FEItem_Shader(description));
    }

    /// <summary>Appends one deferred geometry operation to this filter-effect stream.</summary>
    /// <param name="description">
    /// The non-null immutable geometry contract. Every declared resource must belong to this context's family.
    /// </param>
    public void Geometry(GeometryDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _resourceState.ValidateResources(
            description.Resources,
            static binding => binding.Resource,
            nameof(description));
        AppendDescription(new FEItem_Geometry(description));
    }

    public RenderResource<T> Own<T>(T resource)
        where T : class, IDisposable
    {
        ThrowIfDisposed();
        return _resourceState.Own(resource);
    }

    public RenderResource<T> Borrow<T>(T resource)
        where T : class
    {
        ThrowIfDisposed();
        return _resourceState.Borrow(resource);
    }

    private void AppendDescription(IFEItem item)
    {
        ThrowIfDisposed();
        if (_bounds.IsInvalid)
        {
            _renderTimeItems.Add(item);
            return;
        }

        Rect nextBounds = item.TransformBounds(_bounds);
        _items.Add(item);
        _bounds = nextBounds;
    }

    /// <param name="transformSamplingBounds">
    /// Maps a requested output region to the input region the filter reads while producing it. Omit it when
    /// the footprint is not proven; the region analyzer then materializes the complete input instead of
    /// inferring a footprint from <paramref name="transformBounds"/>, which may be narrower than what the
    /// filter reads.
    /// </param>
    // The factory must not re-enter its executor.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSkiaFilter<T>(T data, Func<T, SKImageFilter?, FilterEffectExecutor, SKImageFilter?> factory,
        Func<T, Rect, Rect> transformBounds, Func<T, Rect, Rect>? transformSamplingBounds = null)
        where T : IEquatable<T>
    {
        AppendDescription(new FEItem_Skia<T>(data, factory, transformBounds)
        {
            TransformSamplingBounds = transformSamplingBounds,
        });
    }

    private void AppendDirectSkiaFilter<T>(
        T data,
        Func<T, SKImageFilter?, SKImageFilter?> factory,
        Func<T, Rect, Rect> transformBounds,
        Func<T, Rect, Rect>? transformSamplingBounds = null)
        where T : IEquatable<T>
    {
        AppendDescription(new FEItem_Skia<T>(
            data,
            (value, input, _) => factory(value, input),
            // A built-in Skia filter only transforms its input, so bounds an earlier filter left empty, such as a crop
            // to an empty rectangle, give it nothing to grow. Inflating them would spread them around their origin and
            // have the renderer allocate and composite a blank target. The mapping lives on the item because the
            // render graph resolves it again from the item and requires the same answer.
            (value, bounds) => bounds.IsEmpty ? bounds : transformBounds(value, bounds))
        {
            DirectFactory = factory,
            TransformSamplingBounds = transformSamplingBounds,
        });
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSKColorFilter<T>(T data, Func<T, FilterEffectExecutor, SKColorFilter?> factory)
        where T : IEquatable<T>
    {
        AddItem(new FEItem_SKColorFilter<T>(data, factory));
    }

    public void CustomEffect<T>(T data, Action<T, CustomFilterEffectContext> action,
        Func<T, Rect, Rect> transformBounds)
        where T : IEquatable<T>
    {
        AppendDescription(new FEItem_CustomEffect<T>(data, action, transformBounds));
    }

    /// <summary>
    /// Appends an opaque custom effect whose output bounds cannot be determined during recording.
    /// </summary>
    /// <remarks>
    /// The unknown bounds remain symbolic through later effects and are resolved to the complete finite local
    /// domain of the owning destination or target scope after enclosing transforms and clips are known. A
    /// target-less root request requires an explicit target domain.
    /// </remarks>
    public void CustomEffect<T>(T data, Action<T, CustomFilterEffectContext> action)
    {
        AddItem(new FEItem_CustomEffect<T>(data, action, null));
        _bounds = Rect.Invalid;
    }

    /// <summary>Records a custom effect with execution-time continuation or composition-target access.</summary>
    public void CustomEffect<T>(T data, Action<T, CustomFilterEffectContext> action,
        CustomFilterEffectOptions options)
    {
        ArgumentNullException.ThrowIfNull(action);
        const CustomFilterEffectOptions supported = CustomFilterEffectOptions.ControlsContinuation
            | CustomFilterEffectOptions.AccessesTarget;
        if ((options & ~supported) != 0) throw new ArgumentOutOfRangeException(nameof(options));
        AddItem(new FEItem_CustomEffect<T>(data, action, null) { Options = options });
        _bounds = Rect.Invalid;
    }

    public int CountItems()
    {
        return _items.Count + _renderTimeItems.Count;
    }

    internal IReadOnlyList<IFEItem> GetOrderedItems()
    {
        ThrowIfDisposed();
        return _renderTimeItems.Count == 0
            ? _items.ToArray()
            : [.. _items, .. _renderTimeItems];
    }

    internal void ApplyTransactional(FilterEffect effect, FilterEffect.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(resource);
        ApplyTransactional(() => effect.ApplyTo(this, resource));
    }

    /// <summary>Appends an effect, retaining its evaluation boundary for a preceding continuation.</summary>
    /// <remarks>
    /// Composite effects should use this method for their children. A continuation may select a different
    /// clock per input; subsequent effects are then reconciled independently against the same scene snapshot.
    /// </remarks>
    public void Apply(FilterEffect.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ThrowIfDisposed();
        if (!resource.IsEnabled) return;
        if (HasContinuation(_items.Span) || HasContinuation(_renderTimeItems.Span))
        {
            ApplyTransactional(() =>
            {
                // Record execution requirements before hiding the child behind its evaluation boundary.
                // Its operations still need to be authored against each continuation's clock and bounds.
                using var child = CreateChildContext();
                child.ApplyTransactional(resource.RequireOriginal(), resource);
                var options = CustomFilterEffectOptions.ControlsContinuation;
                foreach (IFEItem_Custom item in child.GetOrderedItems().OfType<IFEItem_Custom>())
                    options |= item.Options & CustomFilterEffectOptions.AccessesTarget;
                CustomEffect(resource, ApplyContinuationResource, options);
            });
        }
        else
            ApplyTransactional(resource.RequireOriginal(), resource);
    }

    private static bool HasContinuation(ReadOnlySpan<IFEItem> items)
    {
        foreach (IFEItem item in items)
            if (item is IFEItem_Custom custom && custom.Options.HasFlag(CustomFilterEffectOptions.ControlsContinuation))
                return true;
        return false;
    }

    private static void ApplyContinuationResource(FilterEffect.Resource source, CustomFilterEffectContext execution)
    {
        FilterEffect.Resource? evaluated = null;
        try
        {
            if (execution.Instance?.Time is { } time
                && source.EvaluationContext is { } evaluation && time != evaluation.Time)
                evaluated = source.RequireOriginal().ToResource(evaluation.WithTime(time));
            FilterEffect.Resource current = evaluated ?? source;
            using var context = new FilterEffectContext(
                execution.Targets.CalculateBounds(), execution.OutputScale, execution.WorkingScale);
            if (current.IsEnabled)
                context.ApplyTransactional(current.RequireOriginal(), current);
            execution.Continuation?.AppendTo(context);
            using var builder = new SKImageFilterBuilder();
            using var executor = execution.CreateExecutor(execution.Targets, builder);
            executor.Apply(context);
            executor.Flush();
        }
        finally { evaluated?.Dispose(); }
    }

    internal void ApplyTransactional(Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ThrowIfDisposed();

        int itemCount = _items.Count;
        int renderTimeItemCount = _renderTimeItems.Count;
        int resourceCount = _resourceState.Count;
        Rect bounds = _bounds;
        try
        {
            apply();
        }
        catch
        {
            while (_items.Count > itemCount)
                _items.RemoveAt(_items.Count - 1);
            while (_renderTimeItems.Count > renderTimeItemCount)
                _renderTimeItems.RemoveAt(_renderTimeItems.Count - 1);
            _bounds = bounds;
            _resourceState.RollbackTo(resourceCount);
            throw;
        }
    }

    internal void TransferResources() => _resourceState.Transfer();

    internal void PrepareStandaloneResourcesForExecution()
        => _resourceState.CommitStandaloneResources();

    internal static FilterEffectContext CreateEffectItemSegment(
        Rect bounds,
        float outputScale,
        float workingScale,
        IEnumerable<IFEItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var context = new FilterEffectContext(bounds, outputScale, workingScale);
        context.AppendItems(items);
        return context;
    }

    internal void AppendItems(IEnumerable<IFEItem> items)
    {
        bool hasDeferredBounds = false;
        foreach (IFEItem item in items)
        {
            AddItem(item);
            if (item is IFEItem_DeferredBounds)
            {
                // A deferred-bound item resolves its bounds at execution time; authoring it
                // here against the provisional segment input would freeze the wrong matrix.
                hasDeferredBounds = true;
                continue;
            }

            if (!_bounds.IsInvalid)
                _bounds = item.TransformBounds(_bounds);
        }

        // The segment output is only known after the deferred item resolves at execution time.
        if (hasDeferredBounds)
            _bounds = Rect.Invalid;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _items.Dispose();
        _renderTimeItems.Dispose();
        _resourceState.ReleaseReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
