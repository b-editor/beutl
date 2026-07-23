using System.ComponentModel;
using System.Reactive;
using System.Runtime.ExceptionServices;
using Beutl.Collections.Pooled;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Microsoft.Extensions.ObjectPool;
using SkiaSharp;
using FilterEffectOrFEItem = object;

namespace Beutl.Graphics.Effects;

internal sealed class ArrayPooledObjectPolicy<T>(int length) : IPooledObjectPolicy<T[]>
{
    public T[] Create()
    {
        return new T[length];
    }

    public bool Return(T[] obj)
    {
        Array.Clear(obj);
        return true;
    }
}

public sealed class FilterEffectContext : IDisposable
{
    internal readonly PooledList<IFEItem> _items;
    internal readonly PooledList<IFEItem> _renderTimeItems;
    private readonly FilterEffectResourceState _resourceState;
    private readonly Lazy<float> _workingScale;
    private readonly bool _hasResolvedWorkingScale;
    private bool _disposed;

    internal static readonly ObjectPool<float[]> s_colorMatPool;

    static FilterEffectContext()
    {
        s_colorMatPool = new DefaultObjectPool<float[]>(new ArrayPooledObjectPolicy<float>(20));
    }

    public FilterEffectContext(Rect bounds, float outputScale = 1f, float workingScale = 1f)
        : this(
            bounds,
            outputScale,
            CreateResolvedWorkingScale(workingScale),
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
            CreateResolvedWorkingScale(workingScale),
            hasResolvedWorkingScale,
            new FilterEffectResourceState(renderContext))
    {
    }

    internal FilterEffectContext(
        Rect bounds,
        float outputScale,
        Func<float> resolveWorkingScale,
        RenderNodeContext renderContext,
        bool hasResolvedWorkingScale = true)
        : this(
            bounds,
            outputScale,
            new Lazy<float>(resolveWorkingScale ?? throw new ArgumentNullException(nameof(resolveWorkingScale))),
            hasResolvedWorkingScale,
            new FilterEffectResourceState(renderContext))
    {
    }

    private FilterEffectContext(
        Rect bounds,
        float outputScale,
        Lazy<float> workingScale,
        bool hasResolvedWorkingScale,
        FilterEffectResourceState resourceState)
    {
        Bounds = OriginalBounds = bounds;
        OutputScale = outputScale;
        _workingScale = workingScale;
        _hasResolvedWorkingScale = hasResolvedWorkingScale;
        _resourceState = resourceState;
        _renderTimeItems = [];
        _items = [];
    }

    private FilterEffectContext(FilterEffectContext obj)
    {
        OriginalBounds = obj.OriginalBounds;
        Bounds = obj.Bounds;
        OutputScale = obj.OutputScale;
        _workingScale = obj._workingScale;
        _hasResolvedWorkingScale = obj._hasResolvedWorkingScale;
        _resourceState = obj._resourceState.AddReference();
        _renderTimeItems = new PooledList<IFEItem>(obj._renderTimeItems);
        _items = new PooledList<IFEItem>(obj._items);
    }

    private FilterEffectContext(FilterEffectContext obj, Rect bounds)
    {
        OriginalBounds = Bounds = bounds;
        OutputScale = obj.OutputScale;
        _workingScale = obj._workingScale;
        _hasResolvedWorkingScale = obj._hasResolvedWorkingScale;
        _resourceState = obj._resourceState.AddReference();
        _renderTimeItems = [];
        _items = [];
    }

    public Rect Bounds { get; internal set; }

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
    /// <remarks>
    /// A later bounds-expanding operation may apply the per-buffer dimension clamp and run below this nominal
    /// density. Use this value only for scale-independent recording decisions; read the operation-specific density
    /// or actual target scale from the execution-time shader, geometry, or custom-effect context for device math.
    /// A <see langword="false"/> result requires scale-independent recording.
    /// </remarks>
    public bool TryGetWorkingScale(out float workingScale)
    {
        workingScale = _hasResolvedWorkingScale ? _workingScale.Value : default;
        return _hasResolvedWorkingScale;
    }

    private static Lazy<float> CreateResolvedWorkingScale(float workingScale)
        => new(() => workingScale);

    public FilterEffectContext Clone()
    {
        ThrowIfDisposed();
        return new FilterEffectContext(this);
    }

    public FilterEffectContext CreateChildContext()
    {
        ThrowIfDisposed();
        return new FilterEffectContext(this, Bounds);
    }

    private void AddItem(IFEItem item)
    {
        ThrowIfDisposed();
        if (!Bounds.IsInvalid)
        {
            _items.Add(item);
        }
        else
        {
            _renderTimeItems.Add(item);
        }
    }

    public void Shader(ShaderDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _resourceState.ValidateResources(
            description.Resources.Select(static binding => binding.Resource),
            nameof(description));
        AppendDescription(new FEItem_Shader(description));
    }

    public void Geometry(GeometryDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _resourceState.ValidateResources(description.Resources, nameof(description));
        AppendDescription(new FEItem_Geometry(description));
    }

    public RenderResource<T> Own<T>(T resource, object? cacheKey = null, long version = 0)
        where T : class, IDisposable
    {
        ThrowIfDisposed();
        return _resourceState.Own(resource, cacheKey, version);
    }

    public RenderResource<T> Borrow<T>(T resource, object? cacheKey = null, long version = 0)
        where T : class
    {
        ThrowIfDisposed();
        return _resourceState.Borrow(resource, cacheKey, version);
    }

    private void AppendDescription(IFEItem item)
    {
        ThrowIfDisposed();
        if (Bounds.IsInvalid)
        {
            _renderTimeItems.Add(item);
            return;
        }

        Rect nextBounds = item.TransformBounds(Bounds);
        _items.Add(item);
        Bounds = nextBounds;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSkiaFilter<T>(T data, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> factory,
        Func<T, Rect, Rect> transformBounds)
        where T : IEquatable<T>
    {
        AppendDescription(new FEItem_Skia<T>(data, factory, transformBounds));
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSKColorFilter<T>(T data, Func<T, FilterEffectActivator, SKColorFilter?> factory)
        where T : IEquatable<T>
    {
        AddItem(new FEItem_SKColorFilter<T>(data, factory));
    }

    public void DropShadowOnly(Point position, Size sigma, Color color)
    {
        AppendSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input, _) => SKImageFilter.CreateDropShadowOnly(t.position.X, t.position.Y,
                t.sigma.Width, t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3)));
    }

    public void DropShadow(Point position, Size sigma, Color color)
    {
        AppendSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input, _) => SKImageFilter.CreateDropShadow(t.position.X, t.position.Y, t.sigma.Width,
                t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds.Union(bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3))));
    }

    public void Blur(Size sigma)
    {
        if (sigma.Width < 0)
            sigma = sigma.WithWidth(0);
        if (sigma.Height < 0)
            sigma = sigma.WithHeight(0);

        AppendSkiaFilter(
            data: sigma,
            factory: static (sigma, input, _) =>
            {
                if (sigma.Width == 0 && sigma.Height == 0)
                    return null;

                return SKImageFilter.CreateBlur(sigma.Width, sigma.Height, input);
            },
            transformBounds: static (sigma, bounds) =>
                bounds.Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3)));
    }

    // https://github.com/Shopify/react-native-skia/blob/c7740e30234e6b0a49721ab954c4a848e42d7edb/package/src/dom/nodes/paint/ImageFilters.ts#L25
    public void InnerShadow(Point position, Size sigma, Color color)
        => InnerShadowCore(position, sigma, color, Graphics.BlendMode.DstATop);

    public void InnerShadowOnly(Point position, Size sigma, Color color)
        => InnerShadowCore(position, sigma, color, Graphics.BlendMode.DstIn);

    private void InnerShadowCore(Point position, Size sigma, Color color, Graphics.BlendMode blendMode)
    {
        CustomEffect(
            data: (position, sigma, color, blendMode),
            action: (data, context) =>
            {
                for (int i = 0; i < context.Targets.Count; i++)
                {
                    var target = context.Targets[i];
                    if (target.RenderTarget is not null)
                    {
                        EffectTarget newTarget = context.CreateTarget(target.Bounds);
                        using (ImmediateCanvas canvas = context.Open(newTarget))
                        // Source point-blits and sigma/offset are device-px; composite in device space.
                        using (canvas.PushDeviceSpace())
                        {
                            canvas.Clear();
                            // Read density from the target (may be clamped), not context.WorkingScale.
                            float w = newTarget.Scale.Value;
                            using var blur = SKImageFilter.CreateBlur(data.sigma.Width * w, data.sigma.Height * w);
                            using var blend = SKColorFilter.CreateBlendMode(data.color.ToSKColor(), SKBlendMode.SrcOut);
                            using var filter = SKImageFilter.CreateColorFilter(blend, blur);
                            using var paint = new SKPaint { ImageFilter = filter };

                            using (canvas.PushPaint(paint))
                            {
                                canvas.DrawRenderTarget(target.RenderTarget, new Point(data.position.X * w, data.position.Y * w));
                            }

                            using (canvas.PushBlendMode(data.blendMode))
                            {
                                canvas.DrawRenderTarget(target.RenderTarget, default);
                            }
                        }

                        target.Dispose();
                        context.Targets[i] = newTarget;
                    }
                }
            },
            transformBounds: (_, bounds) => bounds);
    }

    public void Transform(Matrix matrix, BitmapInterpolationMode bitmapInterpolationMode)
    {
        AppendSkiaFilter(
            (matrix, bitmapInterpolationMode),
            (data, input, _) => SKImageFilter.CreateMatrix(data.matrix.ToSKMatrix(),
                data.bitmapInterpolationMode.ToSKSamplingOptions(), input),
            (data, rect) => rect.TransformToAABB(data.matrix));
    }

    public void MatrixConvolution(
        PixelSize kernelSize,
        float[] kernel,
        float gain,
        float bias,
        PixelPoint kernelOffset,
        GradientSpreadMethod spreadMethod,
        bool convolveAlpha)
    {
        AppendSkiaFilter(
            (kernelSize, kernel, gain, bias, kernelOffset, spreadMethod, convolveAlpha),
            (data, input, _) => SKImageFilter.CreateMatrixConvolution(
                data.kernelSize.ToSKSizeI(),
                data.kernel,
                data.gain,
                data.bias,
                data.kernelOffset.ToSKPointI(),
                data.spreadMethod.ToSKShaderTileMode(),
                data.convolveAlpha,
                input),
            (data, rect) =>
            {
                Rect dst = rect;
                int w = data.kernelSize.Width - 1;
                int h = data.kernelSize.Height - 1;

                return rect.Inflate(new Thickness(
                    data.kernelOffset.X - w,
                    data.kernelOffset.Y - h,
                    data.kernelOffset.X,
                    data.kernelOffset.Y));
            });
    }

    public void Erode(float radiusX, float radiusY)
    {
        AppendSkiaFilter(
            (radiusX, radiusY),
            (data, input, _) => SKImageFilter.CreateErode(data.radiusX, data.radiusY, input),
            (data, rect) => rect);
    }

    public void Dilate(float radiusX, float radiusY)
    {
        AppendSkiaFilter(
            (radiusX, radiusY),
            (data, input, _) => SKImageFilter.CreateDilate(data.radiusX, data.radiusY, input),
            (data, rect) => rect.Inflate(new Thickness(data.radiusX, data.radiusY)));
    }

    public void ColorMatrix(in ColorMatrix matrix)
    {
        AppendSKColorFilter(matrix, (m, _) =>
        {
            float[] array = s_colorMatPool.Get();
            try
            {
                m.ToArrayForSkia(array);
                return SKColorFilter.CreateColorMatrix(array);
            }
            finally
            {
                s_colorMatPool.Return(array);
            }
        });
    }

    public void ColorMatrix<T>(T data, Func<T, ColorMatrix> factory)
        where T : IEquatable<T>
    {
        AppendSKColorFilter(
            (data, factory),
            (t, _) =>
            {
                float[] array = s_colorMatPool.Get();
                try
                {
                    t.factory.Invoke(t.data).ToArrayForSkia(array);
                    return SKColorFilter.CreateColorMatrix(array);
                }
                finally
                {
                    s_colorMatPool.Return(array);
                }
            });
    }

    public void Saturate(float amount)
    {
        AppendSKColorFilter(amount, (s, _) =>
        {
            float[] array = s_colorMatPool.Get();
            try
            {
                Graphics.ColorMatrix.CreateSaturateMatrix(s, array);
                //M15,M25,M35,M45がゼロなので意味がない
                //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

                return SKColorFilter.CreateColorMatrix(array);
            }
            finally
            {
                s_colorMatPool.Return(array);
            }
        });
    }

    public void HueRotate(float degrees)
    {
        AppendSKColorFilter(degrees, (s, _) =>
        {
            float[] array = s_colorMatPool.Get();
            try
            {
                Graphics.ColorMatrix.CreateHueRotateMatrix(degrees, array);
                //M15,M25,M35,M45がゼロなので意味がない
                //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

                return SKColorFilter.CreateColorMatrix(array);
            }
            finally
            {
                s_colorMatPool.Return(array);
            }
        });
    }

    public void LuminanceToAlpha()
    {
        AppendSKColorFilter(Unit.Default, (_, _) =>
        {
            float[] array = s_colorMatPool.Get();
            try
            {
                Graphics.ColorMatrix.CreateLuminanceToAlphaMatrix(array);
                //M15,M25,M35,M45がゼロなので意味がない
                //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

                return SKColorFilter.CreateColorMatrix(array);
            }
            finally
            {
                s_colorMatPool.Return(array);
            }
        });
    }

    public void Brightness(float amount)
    {
        AppendSKColorFilter(amount, (s, _) =>
        {
            float[] array = s_colorMatPool.Get();
            try
            {
                Graphics.ColorMatrix.CreateBrightness(amount, array);
                //M15,M25,M35,M45がゼロなので意味がない
                //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

                return SKColorFilter.CreateColorMatrix(array);
            }
            finally
            {
                s_colorMatPool.Return(array);
            }
        });
    }

    public void HighContrast(bool grayscale, HighContrastInvertStyle invertStyle, float contrast)
    {
        AppendSKColorFilter(
            (grayscale, invertStyle, contrast),
            (data, _) => SKColorFilter.CreateHighContrast(data.grayscale,
                (SKHighContrastConfigInvertStyle)data.invertStyle, data.contrast));
    }

    public void Lighting(Color multiply, Color add)
    {
        // CreateLightingはsRGBガンマ値でマトリックスを作成するため、
        // リニア色空間では不正確。リニアに変換したカラーマトリックスを使用する。
        AppendSKColorFilter(
            (multiply, add),
            (data, _) =>
            {
                var mulLinear = data.multiply.ToLinear();
                var addLinear = data.add.ToLinear();

                float[] array = s_colorMatPool.Get();
                try
                {
                    array.AsSpan().Clear();
                    array[0] = mulLinear.X;
                    array[6] = mulLinear.Y;
                    array[12] = mulLinear.Z;
                    array[18] = 1;
                    array[4] = addLinear.X;
                    array[9] = addLinear.Y;
                    array[14] = addLinear.Z;
                    return SKColorFilter.CreateColorMatrix(array);
                }
                finally
                {
                    s_colorMatPool.Return(array);
                }
            });
    }

    public void LumaColor()
    {
        AppendSKColorFilter(Unit.Default, (_, _) => SKColorFilter.CreateLumaColor());
    }

    public void BlendMode(Color color, BlendMode blendMode)
    {
        AppendSKColorFilter(
            (color, blendMode),
            (data, _) => SKColorFilter.CreateBlendMode(data.color.ToSKColor(), (SKBlendMode)data.blendMode));
    }

    public void BlendMode(Brush.Resource? brush, BlendMode blendMode)
    {
        static void ApplyCore((Brush.Resource? Brush, BlendMode BlendMode) data, CustomFilterEffectContext context)
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                var target = context.Targets[i];
                if (target.RenderTarget is not null)
                {
                    Size size = target.Bounds.Size;
                    EffectTarget newTarget = context.CreateTarget(target.Bounds);
                    // Read density from the target (may be clamped), not context.WorkingScale.
                    float w = newTarget.Scale.Value;
                    var c = new BrushConstructor(new(size), data.Brush, data.BlendMode, w, context.MaxWorkingScale);
                    using var brushPaint = new SKPaint();
                    c.ConfigurePaint(brushPaint);

                    using (ImmediateCanvas newCanvas = context.Open(newTarget))
                    {
                        newCanvas.Clear();
                        // Source is a device-px point-blit; enter device space.
                        using (newCanvas.PushDeviceSpace())
                        {
                            newCanvas.DrawRenderTarget(target.RenderTarget, default);
                        }

                        newCanvas.Canvas.DrawRect(SKRect.Create(size.ToSKSize()), brushPaint);
                    }

                    target.Dispose();
                    context.Targets[i] = newTarget;
                }
            }
        }

        CustomEffect((brush, blendMode), ApplyCore, (_, r) => r);
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
        Bounds = Rect.Invalid;
    }

    public int CountItems()
    {
        return _items.Count;
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

    internal void ApplyTransactional(Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ThrowIfDisposed();

        int itemCount = _items.Count;
        int renderTimeItemCount = _renderTimeItems.Count;
        int resourceCount = _resourceState.Count;
        Rect bounds = Bounds;
        try
        {
            apply();
        }
        catch (Exception ex)
        {
            ExceptionDispatchInfo primary = ExceptionDispatchInfo.Capture(ex);
            while (_items.Count > itemCount)
                _items.RemoveAt(_items.Count - 1);
            while (_renderTimeItems.Count > renderTimeItemCount)
                _renderTimeItems.RemoveAt(_renderTimeItems.Count - 1);
            Bounds = bounds;
            try
            {
                _resourceState.RollbackTo(resourceCount);
            }
            catch (Exception cleanupFailure)
            {
                const string key = "FilterEffectResourceRollbackFailure";
                ex.Data[key] = ex.Data[key] is Exception previousFailure
                    ? new AggregateException(
                        "Multiple filter-effect resource rollback failures occurred.",
                        previousFailure,
                        cleanupFailure)
                    : cleanupFailure;
            }

            primary.Throw();
        }
    }

    internal void TransferResources() => _resourceState.Transfer();

    internal static FilterEffectContext CreateLegacySegment(
        Rect bounds,
        float outputScale,
        float workingScale,
        IEnumerable<IFEItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var context = new FilterEffectContext(bounds, outputScale, workingScale);
        foreach (IFEItem item in items)
        {
            context.AddItem(item);
            if (!context.Bounds.IsInvalid)
                context.Bounds = item.TransformBounds(context.Bounds);
        }

        return context;
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

internal sealed class FilterEffectResourceState
{
    private readonly RenderNodeContext? _renderContext;
    private readonly RenderRequestResourceRegistry? _standaloneRegistry;
    private readonly List<RenderResource> _resources = [];
    private int _references = 1;
    private bool _transferred;

    public FilterEffectResourceState(RenderNodeContext? renderContext)
    {
        _renderContext = renderContext;
        if (renderContext is null)
            _standaloneRegistry = new RenderRequestResourceRegistry();
    }

    public int Count => _resources.Count;

    public FilterEffectResourceState AddReference()
    {
        if (_references <= 0)
            throw new ObjectDisposedException(nameof(FilterEffectResourceState));
        _references++;
        return this;
    }

    public RenderResource<T> Own<T>(T resource, object? cacheKey, long version)
        where T : class, IDisposable
    {
        ThrowIfTransferred();
        RenderResource<T> token = _renderContext is not null
            ? _renderContext.Own(resource, cacheKey, version)
            : _standaloneRegistry!.RegisterOwned(resource, cacheKey, version);
        _resources.Add(token);
        return token;
    }

    public RenderResource<T> Borrow<T>(T resource, object? cacheKey, long version)
        where T : class
    {
        ThrowIfTransferred();
        RenderResource<T> token = _renderContext is not null
            ? _renderContext.Borrow(resource, cacheKey, version)
            : _standaloneRegistry!.RegisterBorrowed(resource, cacheKey, version);
        _resources.Add(token);
        return token;
    }

    public void ValidateResources(IEnumerable<RenderResource> resources, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (RenderResource resource in resources)
        {
            if (!_resources.Any(item => ReferenceEquals(item.SlotIdentity, resource.SlotIdentity))
                || resource.RegistrationState == RenderResourceRegistrationState.Released)
            {
                throw new ArgumentException(
                    "Every declared resource must be registered by this FilterEffectContext family.",
                    parameterName);
            }
        }
    }

    public void RollbackTo(int count)
    {
        if (count < 0 || count > _resources.Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == _resources.Count)
            return;

        RenderResource[] removed = _resources.Skip(count).ToArray();
        _resources.RemoveRange(count, _resources.Count - count);
        if (_renderContext is not null)
        {
            _renderContext.RollbackResources(removed);
            return;
        }

        List<Exception>? failures = null;
        for (int index = removed.Length - 1; index >= 0; index--)
        {
            try
            {
                _standaloneRegistry!.Rollback(removed[index]);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("Filter-effect resource rollback failed.", failures);
    }

    public void Transfer()
    {
        ThrowIfTransferred();
        _transferred = true;
    }

    public void ReleaseReference()
    {
        if (_references <= 0)
            return;
        _references--;
        if (_references != 0)
            return;

        try
        {
            if (!_transferred)
                RollbackTo(0);
        }
        finally
        {
            _standaloneRegistry?.Dispose();
        }
    }

    private void ThrowIfTransferred()
    {
        if (_transferred)
            throw new InvalidOperationException("Filter-effect resources were already transferred to the render request.");
    }
}
