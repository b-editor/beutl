using System.ComponentModel;
using System.Reactive;
using System.Runtime.ExceptionServices;
using Beutl.Collections.Pooled;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
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
        _bounds = OriginalBounds = bounds;
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
        _bounds = obj._bounds;
        OutputScale = obj.OutputScale;
        _workingScale = obj._workingScale;
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
        _workingScale = obj._workingScale;
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

    // The value-taking constructor: the scale is already resolved, so wrapping it costs one object rather
    // than the closure and Func a factory overload would also allocate on every recorded effect.
    private static Lazy<float> CreateResolvedWorkingScale(float workingScale)
        => new(workingScale);

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
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSkiaFilter<T>(T data, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> factory,
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
            transformBounds)
        {
            DirectFactory = factory,
            TransformSamplingBounds = transformSamplingBounds,
        });
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSKColorFilter<T>(T data, Func<T, FilterEffectActivator, SKColorFilter?> factory)
        where T : IEquatable<T>
    {
        AddItem(new FEItem_SKColorFilter<T>(data, factory));
    }

    public void DropShadowOnly(Point position, Size sigma, Color color)
    {
        AppendDirectSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input) => SKImageFilter.CreateDropShadowOnly(t.position.X, t.position.Y,
                t.sigma.Width, t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3)),
            transformSamplingBounds: static (t, region) => region
                .Translate(-t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3)));
    }

    public void DropShadow(Point position, Size sigma, Color color)
    {
        AppendDirectSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input) => SKImageFilter.CreateDropShadow(t.position.X, t.position.Y, t.sigma.Width,
                t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds.Union(bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3))),
            transformSamplingBounds: static (t, region) => region.Union(region
                .Translate(-t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3))));
    }

    public void Blur(Size sigma)
    {
        if (sigma.Width < 0)
            sigma = sigma.WithWidth(0);
        if (sigma.Height < 0)
            sigma = sigma.WithHeight(0);

        AppendDirectSkiaFilter(
            data: sigma,
            factory: static (sigma, input) =>
            {
                if (sigma.Width == 0 && sigma.Height == 0)
                    return null;

                return SKImageFilter.CreateBlur(sigma.Width, sigma.Height, input);
            },
            transformBounds: static (sigma, bounds) =>
                bounds.Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3)),
            transformSamplingBounds: static (sigma, region) =>
                region.Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3)));
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
                        if (newTarget.IsEmpty)
                        {
                            newTarget.Dispose();
                            continue;
                        }

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
        // No sampling footprint: the resampling apron is a device-pixel quantity, and the density the
        // segment finally runs at is unknown here, so no logical margin can bound it.
        AppendDirectSkiaFilter(
            (matrix, bitmapInterpolationMode),
            (data, input) => SKImageFilter.CreateMatrix(data.matrix.ToSKMatrix(),
                data.bitmapInterpolationMode.ToSKSamplingOptions(), input),
            (data, rect) => rect.TransformToAABB(data.matrix));
    }

    /// <summary>
    /// Appends a Skia matrix image filter whose matrix is resolved from the execution-time target
    /// bounds via <paramref name="matrixFactory"/> when the input bounds are symbolic.
    /// </summary>
    /// <remarks>
    /// When <see cref="Bounds"/> is concrete the matrix is resolved from it immediately, matching
    /// <see cref="Transform(Matrix, BitmapInterpolationMode)"/>. When it is
    /// <see cref="Rect.Invalid"/> (symbolic owning-domain input) the recorded item stays unresolved:
    /// each activation resolves one matrix from its own combined execution-time target bounds and
    /// maps every target of that activation with it.
    /// </remarks>
    public void Transform<T>(T data, Func<T, Rect, Matrix> matrixFactory,
        BitmapInterpolationMode bitmapInterpolationMode)
        where T : IEquatable<T>
    {
        ArgumentNullException.ThrowIfNull(matrixFactory);
        if (!_bounds.IsInvalid)
        {
            Transform(matrixFactory(data, _bounds), bitmapInterpolationMode);
            return;
        }

        AppendDescription(new FEItem_SkiaDeferredMatrix<T>(data, matrixFactory, bitmapInterpolationMode));
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
        // No sampling footprint: the spread method resolves against the extent of whatever input it is
        // given, so a cropped input would change the result inside the requested region.
        AppendDirectSkiaFilter(
            (kernelSize, kernel, gain, bias, kernelOffset, spreadMethod, convolveAlpha),
            (data, input) => SKImageFilter.CreateMatrixConvolution(
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
        if (!TryClampMorphologyRadius(ref radiusX, ref radiusY))
            return;

        AppendDirectSkiaFilter(
            (radiusX, radiusY),
            (data, input) => SKImageFilter.CreateErode(data.radiusX, data.radiusY, input),
            (data, rect) => rect,
            // Erode shrinks its declared output but still reads the whole radius neighbourhood.
            (data, region) => region.Inflate(new Thickness(data.radiusX, data.radiusY)));
    }

    public void Dilate(float radiusX, float radiusY)
    {
        if (!TryClampMorphologyRadius(ref radiusX, ref radiusY))
            return;

        AppendDirectSkiaFilter(
            (radiusX, radiusY),
            (data, input) => SKImageFilter.CreateDilate(data.radiusX, data.radiusY, input),
            (data, rect) => rect.Inflate(new Thickness(data.radiusX, data.radiusY)),
            (data, region) => region.Inflate(new Thickness(data.radiusX, data.radiusY)));
    }

    // Skia rejects a negative morphology radius, so it degrades to a pass-through. The all-zero
    // case records no stage rather than an identity one because a degenerate stage still re-grids
    // the content through an intermediate and shifts antialiased edges.
    private static bool TryClampMorphologyRadius(ref float radiusX, ref float radiusY)
    {
        radiusX = MathF.Max(radiusX, 0);
        radiusY = MathF.Max(radiusY, 0);
        return radiusX != 0 || radiusY != 0;
    }

    public void ColorMatrix(in ColorMatrix matrix)
    {
        if (matrix.IsIdentity)
            return;

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
        ArgumentNullException.ThrowIfNull(factory);
        ColorMatrix(factory(data));
    }

    public void Saturate(float amount)
    {
        float[] array = s_colorMatPool.Get();
        try
        {
            Graphics.ColorMatrix.CreateSaturateMatrix(amount, array);
            //M15,M25,M35,M45がゼロなので意味がない
            //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

            ShaderColorMatrix(array);
        }
        finally
        {
            s_colorMatPool.Return(array);
        }
    }

    public void HueRotate(float degrees)
    {
        float[] array = s_colorMatPool.Get();
        try
        {
            Graphics.ColorMatrix.CreateHueRotateMatrix(degrees, array);
            //M15,M25,M35,M45がゼロなので意味がない
            //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

            ShaderColorMatrix(array);
        }
        finally
        {
            s_colorMatPool.Return(array);
        }
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
        // Recorded as a CurrentPixel shader stage rather than a Skia color filter so that an adjacent shader
        // stage can fuse with it instead of splitting the chain at a effect-item segment.
        float[] array = s_colorMatPool.Get();
        try
        {
            Graphics.ColorMatrix.CreateBrightness(amount, array);
            //M15,M25,M35,M45がゼロなので意味がない
            //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

            ShaderColorMatrix(array);
        }
        finally
        {
            s_colorMatPool.Return(array);
        }
    }

    public void HighContrast(bool grayscale, HighContrastInvertStyle invertStyle, float contrast)
    {
        // SKColorFilter.CreateHighContrast returns null for an invalid configuration, which made the old path a
        // no-op. Preserve that behavior instead of recording a shader with undefined parameters.
        if (!Enum.IsDefined(invertStyle) || float.IsNaN(contrast) || contrast is < -1f or > 1f)
            return;

        Shader(BuiltInColorFilterShader.HighContrast(grayscale, invertStyle, contrast));
    }

    public void Lighting(Color multiply, Color add)
    {
        // CreateLightingはsRGBガンマ値でマトリックスを作成するため、
        // リニア色空間では不正確。リニアに変換したカラーマトリックスを使用する。
        var mulLinear = multiply.ToLinear();
        var addLinear = add.ToLinear();

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
            ShaderColorMatrix(array);
        }
        finally
        {
            s_colorMatPool.Return(array);
        }
    }

    public void LumaColor()
    {
        Shader(BuiltInColorFilterShader.LumaColor());
    }

    private void ShaderColorMatrix(ReadOnlySpan<float> matrix)
    {
        if (!Graphics.ColorMatrix.CreateFromSpan(matrix).IsIdentity)
            Shader(ColorMatrixShader.CurrentPixel(matrix));
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
                    if (newTarget.IsEmpty)
                    {
                        newTarget.Dispose();
                        continue;
                    }

                    // Read density from the target (may be clamped), not context.WorkingScale.
                    float w = newTarget.Scale.Value;
                    using var brushPaint = new SKPaint();
                    context.CreateBrushConstructor(
                        new Rect(size),
                        data.Brush,
                        data.BlendMode,
                        w).ConfigurePaint(brushPaint);

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
        catch (Exception ex)
        {
            ExceptionDispatchInfo primary = ExceptionDispatchInfo.Capture(ex);
            while (_items.Count > itemCount)
                _items.RemoveAt(_items.Count - 1);
            while (_renderTimeItems.Count > renderTimeItemCount)
                _renderTimeItems.RemoveAt(_renderTimeItems.Count - 1);
            _bounds = bounds;
            try
            {
                _resourceState.RollbackTo(resourceCount, ex);
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
        bool hasDeferredBounds = false;
        foreach (IFEItem item in items)
        {
            context.AddItem(item);
            if (item is IFEItem_Skia { ResolveBoundsAtExecutionTime: true })
            {
                // A deferred-bound item resolves its bounds at execution time; authoring it
                // here against the provisional segment input would freeze the wrong matrix.
                hasDeferredBounds = true;
                continue;
            }

            if (!context._bounds.IsInvalid)
                context._bounds = item.TransformBounds(context._bounds);
        }

        // The segment output is only known after the deferred item resolves at execution time.
        if (hasDeferredBounds)
            context._bounds = Rect.Invalid;

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

    public RenderResource<T> Own<T>(T resource)
        where T : class, IDisposable
    {
        ThrowIfTransferred();
        RenderResource<T> token = _renderContext is not null
            ? _renderContext.Own(resource)
            : _standaloneRegistry!.RegisterOwned(resource);
        _resources.Add(token);
        return token;
    }

    public RenderResource<T> Borrow<T>(T resource)
        where T : class
    {
        ThrowIfTransferred();
        RenderResource<T> token = _renderContext is not null
            ? _renderContext.Borrow(resource)
            : _standaloneRegistry!.RegisterBorrowed(resource);
        _resources.Add(token);
        return token;
    }

    /// <remarks>
    /// Shader and geometry stages declare their bindings as different types, so the resource is read through a
    /// selector rather than by projecting each list into a common one.
    /// </remarks>
    public void ValidateResources<TBinding>(
        IReadOnlyList<TBinding> bindings,
        Func<TBinding, RenderResource> selectResource,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(selectResource);
        for (int index = 0; index < bindings.Count; index++)
        {
            RenderResource resource = selectResource(bindings[index]);
            if (!IsRegistered(resource)
                || resource.RegistrationState == RenderResourceRegistrationState.Released)
            {
                throw new ArgumentException(
                    "Every declared resource must be registered by this FilterEffectContext family.",
                    parameterName);
            }
        }
    }

    private bool IsRegistered(RenderResource resource)
    {
        for (int index = 0; index < _resources.Count; index++)
        {
            if (ReferenceEquals(_resources[index].SlotIdentity, resource.SlotIdentity))
                return true;
        }

        return false;
    }

    public void RollbackTo(int count, Exception? primaryFailure = null)
    {
        if (count < 0 || count > _resources.Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == _resources.Count)
            return;

        RenderResource[] removed = _resources.Skip(count).ToArray();
        _resources.RemoveRange(count, _resources.Count - count);
        Rollback(removed, primaryFailure);
    }

    private void Rollback(RenderResource[] removed, Exception? primaryFailure)
    {
        if (_renderContext is not null)
        {
            if (primaryFailure is null)
                _renderContext.RollbackResources(removed);
            else
            {
                Exception? cleanupFailure =
                    _renderContext.RollbackResourcesAndCapture(removed, primaryFailure);
                if (cleanupFailure is not null)
                    throw cleanupFailure;
            }

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

    public void CommitStandaloneResources()
    {
        if (_standaloneRegistry is null)
            return;

        foreach (RenderResource resource in _resources)
        {
            if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
                _standaloneRegistry.Commit(resource);
        }
    }

    public void ReleaseReference()
    {
        if (_references <= 0)
            return;
        _references--;
        if (_references != 0)
            return;

        if (_standaloneRegistry is not null)
        {
            _standaloneRegistry.Dispose();
            return;
        }

        if (!_transferred)
            RollbackTo(0);
    }

    private void ThrowIfTransferred()
    {
        if (_transferred)
            throw new InvalidOperationException("Filter-effect resources were already transferred to the render request.");
    }
}

internal record FEItem_Skia<T>(
    T Data, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> Factory, Func<T, Rect, Rect> TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Skia
{
    public Func<T, SKImageFilter?, SKImageFilter?>? DirectFactory { get; init; }

    /// <summary>
    /// Always <see langword="false"/>: this item's mapping is fixed at construction. Deferral is
    /// <see cref="IFEItem_DeferredBounds"/>, which hands each activation its own resolution instead of
    /// letting one recorded item carry the first activation's.
    /// </summary>
    public bool ResolveBoundsAtExecutionTime => false;

    /// <summary>
    /// Maps a requested output region to the input region the built <see cref="SKImageFilter"/> reads, or
    /// <see langword="null"/> when the footprint is not proven.
    /// </summary>
    public Func<T, Rect, Rect>? TransformSamplingBounds { get; init; }

    public bool TryTransformSamplingBounds(Rect output, out Rect input)
    {
        if (TransformSamplingBounds is null)
        {
            input = default;
            return false;
        }

        input = TransformSamplingBounds(Data, output);
        return true;
    }

    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
    {
        builder.AppendSkiaFilter(Data, activator, Factory);
    }

    public bool SupportsDirectReplay => DirectFactory is not null;

    public void AcceptsDirect(SKImageFilterBuilder builder)
    {
        builder.AppendSkiaFilter(Data, DirectFactory!);
    }
}

internal record FEItem_SKColorFilter<T>(
    T Data, Func<T, FilterEffectActivator, SKColorFilter?> Factory)
    : FEItem<T>(Data, (_, rect) => rect), IFEItem_Skia
{
    public bool ResolveBoundsAtExecutionTime => false;

    public bool TryTransformSamplingBounds(Rect output, out Rect input)
    {
        // A color filter is evaluated per pixel, so it never reads outside the requested region.
        input = output;
        return true;
    }

    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
    {
        builder.AppendSKColorFilter(Data, activator, Factory);
    }

    public bool SupportsDirectReplay => false;

    public void AcceptsDirect(SKImageFilterBuilder builder)
        => throw new InvalidOperationException("This color filter has no direct-replay factory.");
}

/// <summary>
/// A matrix filter whose matrix is resolved from the combined execution-time target bounds, because its
/// origin depends on input bounds a preceding custom effect may only re-target at execution time.
/// </summary>
internal sealed record FEItem_SkiaDeferredMatrix<T>(
    T Data,
    Func<T, Rect, Matrix> MatrixFactory,
    BitmapInterpolationMode InterpolationMode) : IFEItem_DeferredBounds
{
    public bool ResolveBoundsAtExecutionTime => true;

    public bool SupportsDirectReplay => false;

    // Unresolved, the mapping is unknown; a recording-time bounds walk that took a concrete answer here
    // would freeze a matrix built from provisional bounds.
    Rect IFEItem.TransformBounds(Rect bounds) => Rect.Invalid;

    public bool TryTransformSamplingBounds(Rect output, out Rect input)
    {
        // No sampling footprint: the resampling apron is a device-pixel quantity, and the density the
        // segment finally runs at is unknown here, so no logical margin can bound it.
        input = default;
        return false;
    }

    public IFEItem_Skia ResolveForActivation(Rect targetBounds)
    {
        Matrix matrix = MatrixFactory(Data, targetBounds);
        return new FEItem_Skia<(Matrix Matrix, BitmapInterpolationMode InterpolationMode)>(
            (matrix, InterpolationMode),
            static (d, input, _) => SKImageFilter.CreateMatrix(
                d.Matrix.ToSKMatrix(), d.InterpolationMode.ToSKSamplingOptions(), input),
            static (d, rect) => rect.IsInvalid ? Rect.Invalid : rect.TransformToAABB(d.Matrix));
    }

    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
        => throw new InvalidOperationException(
            "A deferred-bound item runs only through the resolution of one activation.");

    public void AcceptsDirect(SKImageFilterBuilder builder)
        => throw new InvalidOperationException("A deferred-bound matrix item has no direct-replay factory.");
}

internal record FEItem_CustomEffect<T>(
    T Data, Action<T, CustomFilterEffectContext> Action, Func<T, Rect, Rect>? TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Custom
{
    public void Accepts(CustomFilterEffectContext context)
    {
        Action.Invoke(Data, context);
    }
}
