using System.ComponentModel;
using System.Reactive;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Collections.Pooled;
using Beutl.Media;

using Microsoft.Extensions.ObjectPool;

using SkiaSharp;

using FilterEffectOrFEItemWrapper = object;

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

internal record FEItemWrapper(
    IFEItem Item,
    FilterEffect? FilterEffect,
    int Version,
    // このIFEItemがTransformBoundsする前のBounds
    Rect SourceBounds)
{
    // Todo: 後で削除
    public FEItemWrapper Inherit()
    {
        return this;
    }
}

public sealed class FilterEffectContext : IDisposable
{
    internal readonly PooledList<FEItemWrapper> _items;
    internal readonly PooledList<FilterEffectOrFEItemWrapper> _renderTimeItems;

    internal FilterEffect? _current;

    internal static readonly ObjectPool<byte[]> s_lutPool;
    internal static readonly ObjectPool<float[]> s_colorMatPool;

    static FilterEffectContext()
    {
        s_lutPool = new DefaultObjectPool<byte[]>(new ArrayPooledObjectPolicy<byte>(256));
        s_colorMatPool = new DefaultObjectPool<float[]>(new ArrayPooledObjectPolicy<float>(20));
    }

    public FilterEffectContext(Rect bounds)
    {
        Bounds = OriginalBounds = bounds;
        _renderTimeItems = [];
        _items = [];
    }

    private FilterEffectContext(FilterEffectContext obj)
    {
        OriginalBounds = obj.OriginalBounds;
        Bounds = obj.Bounds;
        _renderTimeItems = new PooledList<FilterEffectOrFEItemWrapper>(obj._renderTimeItems);
        _items = new PooledList<FEItemWrapper>(obj._items);
    }

    public Rect Bounds { get; internal set; }

    public Rect OriginalBounds { get; }

    public FilterEffectContext Clone()
    {
        return new FilterEffectContext(this);
    }

    public FilterEffectContext CreateChildContext()
    {
        // 今はnewしているが、キャッシュする予定
        return new FilterEffectContext(Bounds);
    }

    private void AddItem(IFEItem item)
    {
        if (!Bounds.IsInvalid)
        {
            _items.Add(new FEItemWrapper(item, _current, _current?.Version ?? 0, Bounds));
        }
        else
        {
            _renderTimeItems.Add(new FEItemWrapper(item, _current, _current?.Version ?? 0, Bounds));
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSkiaFilter<T>(T data, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> factory, Func<T, Rect, Rect> transformBounds)
        where T : IEquatable<T>
    {
        AddItem(new FEItem_Skia<T>(data, factory, transformBounds));
        Bounds = transformBounds.Invoke(data, Bounds);
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
            factory: static (t, input, _) => SKImageFilter.CreateDropShadowOnly(t.position.X, t.position.Y, t.sigma.Width, t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3)));
    }

    [Obsolete("Use DropShadowOnly(Point, Size, Color)")]
    public void DropShadowOnly(Point position, Vector sigma, Color color)
    {
        DropShadowOnly(position, new Size(sigma.X, sigma.Y), color);
    }

    public void DropShadow(Point position, Size sigma, Color color)
    {
        AppendSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input, _) => SKImageFilter.CreateDropShadow(t.position.X, t.position.Y, t.sigma.Width, t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds.Union(bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.Width * 3, t.sigma.Height * 3))));
    }

    [Obsolete("Use DropShadow(Point, Size, Color)")]
    public void DropShadow(Point position, Vector sigma, Color color)
    {
        DropShadow(position, new Size(sigma.X, sigma.Y), color);
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
            transformBounds: static (sigma, bounds) => bounds.Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3)));
    }

    [Obsolete("Use Blur(Size)")]
    public void Blur(Vector sigma)
    {
        Blur(new Size(sigma.X, sigma.Y));
    }

    public void DisplacementMap(
        SKColorChannel xChannelSelector,
        SKColorChannel yChannelSelector,
        float scale,
        FilterEffect displacement)
    {
        DisplacementMap(xChannelSelector, yChannelSelector, scale, context => context.Apply(displacement));
    }

    public void DisplacementMap(
        SKColorChannel xChannelSelector,
        SKColorChannel yChannelSelector,
        float scale,
        Action<FilterEffectContext> displacementFactory)
    {
        FilterEffectContext child = CreateChildContext();
        displacementFactory.Invoke(child);

        AppendSkiaFilter(
            data: (xChannelSelector, yChannelSelector, scale, child),
            factory: static (t, input, activator)
                => SKImageFilter.CreateDisplacementMapEffect(t.xChannelSelector, t.yChannelSelector, t.scale, activator.Activate(t.child), input),
            transformBounds: static (data, bounds) => bounds.Inflate(data.scale / 2));
    }

    // https://github.com/Shopify/react-native-skia/blob/c7740e30234e6b0a49721ab954c4a848e42d7edb/package/src/dom/nodes/paint/ImageFilters.ts#L25
    public void InnerShadow(Point position, Size sigma, Color color)
    {
        CustomEffect(
            data: (position, sigma, color),
            action: (data, context) =>
            {
                for (int i = 0; i < context.Targets.Count; i++)
                {
                    var target = context.Targets[i];
                    if (target.Surface is { } srcSurface)
                    {
                        using SKImage skimage = target.Surface.Value.Snapshot();
                        EffectTarget newTarget = context.CreateTarget(target.Bounds);
                        using (ImmediateCanvas canvas = context.Open(newTarget))
                        {
                            using var blur = SKImageFilter.CreateBlur(data.sigma.Width, data.sigma.Height);
                            using var blend = SKColorFilter.CreateBlendMode(data.color.ToSKColor(), SKBlendMode.SrcOut);
                            using var filter = SKImageFilter.CreateColorFilter(blend, blur);
                            using var paint = new SKPaint
                            {
                                ImageFilter = filter
                            };

                            using (canvas.PushPaint(paint))
                            {
                                canvas.DrawSurface(target.Surface.Value, data.position);
                            }

                            using (canvas.PushBlendMode(Graphics.BlendMode.DstATop))
                            {
                                canvas.DrawSurface(target.Surface.Value, default);
                            }
                        }

                        target.Dispose();
                        context.Targets[i] = newTarget;
                    }
                }
            },
            transformBounds: (_, bounds) => bounds);
    }

    [Obsolete("Use InnerShadow(Point, Size, Color)")]
    public void InnerShadow(Point position, Vector sigma, Color color)
    {
        InnerShadow(position, new Size(sigma.X, sigma.Y), color);
    }

    public void InnerShadowOnly(Point position, Size sigma, Color color)
    {
        CustomEffect(
            data: (position, sigma, color),
            action: (data, context) =>
            {
                for (int i = 0; i < context.Targets.Count; i++)
                {
                    var target = context.Targets[i];
                    if (target.Surface is { } srcSurface)
                    {
                        using SKImage skimage = target.Surface.Value.Snapshot();
                        EffectTarget newTarget = context.CreateTarget(target.Bounds);
                        using (ImmediateCanvas canvas = context.Open(newTarget))
                        {
                            using var blur = SKImageFilter.CreateBlur(data.sigma.Width, data.sigma.Height);
                            using var blend = SKColorFilter.CreateBlendMode(data.color.ToSKColor(), SKBlendMode.SrcOut);
                            using var filter = SKImageFilter.CreateColorFilter(blend, blur);
                            using var paint = new SKPaint
                            {
                                ImageFilter = filter
                            };

                            using (canvas.PushPaint(paint))
                            {
                                canvas.DrawSurface(target.Surface.Value, data.position);
                            }

                            using (canvas.PushBlendMode(Graphics.BlendMode.DstIn))
                            {
                                canvas.DrawSurface(target.Surface.Value, default);
                            }
                        }

                        target.Dispose();
                        context.Targets[i] = newTarget;
                    }
                }
            },
            transformBounds: (_, bounds) => bounds);
    }

    [Obsolete("Use InnerShadowOnly(Point, Size, Color)")]
    public void InnerShadowOnly(Point position, Vector sigma, Color color)
    {
        InnerShadowOnly(position, new Size(sigma.X, sigma.Y), color);
    }

    public void Transform(Matrix matrix, BitmapInterpolationMode bitmapInterpolationMode)
    {
        AppendSkiaFilter(
            (matrix, bitmapInterpolationMode),
            (data, input, _) => SKImageFilter.CreateMatrix(data.matrix.ToSKMatrix(), data.bitmapInterpolationMode.ToSKFilterQuality(), input),
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
            (data, _) => SKColorFilter.CreateHighContrast(data.grayscale, (SKHighContrastConfigInvertStyle)data.invertStyle, data.contrast));
    }

    public void Lighting(Color multiply, Color add)
    {
        AppendSKColorFilter(
            (multiply, add),
            (data, _) => SKColorFilter.CreateLighting(data.multiply.ToSKColor(), data.add.ToSKColor()));
    }

    public void LumaColor()
    {
        AppendSKColorFilter(Unit.Default, (_, _) => SKColorFilter.CreateLumaColor());
    }

    public void LookupTable<T>(
        T data,
        float strength,
        Action<T, (byte[] A, byte[] R, byte[] G, byte[] B)> factory)
        where T : IEquatable<T>
    {
        AppendSKColorFilter((data, strength, factory), (data, _) =>
        {
            byte[] a = s_lutPool.Get();
            byte[] r = s_lutPool.Get();
            byte[] g = s_lutPool.Get();
            byte[] b = s_lutPool.Get();

            try
            {
                data.factory(data.data, (a, r, g, b));

                Graphics.LookupTable.SetStrength(data.strength, (a, r, g, b));
                return SKColorFilter.CreateTable(a, r, g, b);
            }
            finally
            {
                s_lutPool.Return(a);
                s_lutPool.Return(r);
                s_lutPool.Return(g);
                s_lutPool.Return(b);
            }
        });
    }

    public void LookupTable<T>(
        T data,
        float strength,
        Action<T, byte[]> factory)
        where T : IEquatable<T>
    {
        AppendSKColorFilter((data, strength, factory), (data, _) =>
        {
            byte[] array = s_lutPool.Get();

            try
            {
                data.factory(data.data, array);

                Graphics.LookupTable.SetStrength(data.strength, array);
                return SKColorFilter.CreateTable(array);
            }
            finally
            {
                s_lutPool.Return(array);
            }
        });
    }

    public void BlendMode(Color color, BlendMode blendMode)
    {
        AppendSKColorFilter(
            (color, blendMode),
            (data, _) => SKColorFilter.CreateBlendMode(data.color.ToSKColor(), (SKBlendMode)data.blendMode));
    }

    public void BlendMode(IBrush? brush, BlendMode blendMode)
    {
        static void ApplyCore((IBrush? Brush, BlendMode BlendMode) data, CustomFilterEffectContext context)
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                var target = context.Targets[i];
                if (target.Surface is { } srcSurface)
                {
                    Size size = target.Bounds.Size;
                    EffectTarget newTarget = context.CreateTarget(target.Bounds);
                    using ImmediateCanvas newCanvas = context.Open(newTarget);

                    var c = new BrushConstructor(new(size), data.Brush, data.BlendMode, newCanvas);
                    using var brushPaint = new SKPaint();
                    c.ConfigurePaint(brushPaint);

                    newCanvas.DrawSurface(srcSurface.Value, default);
                    newCanvas.Canvas.DrawRect(SKRect.Create(size.ToSKSize()), brushPaint);

                    target.Dispose();
                    context.Targets[i] = newTarget;
                }
            }
        }

        CustomEffect((brush, blendMode), ApplyCore, (_, r) => r);
    }

    [Obsolete("Use CustomEffect")]
    public void Custom<T>(T data, Action<T, FilterEffectCustomOperationContext> action, Func<T, Rect, Rect> transformBounds)
        where T : IEquatable<T>
    {
        AddItem(new FEItem_Custom<T>(data, action, transformBounds));
        Bounds = transformBounds.Invoke(data, Bounds);
    }

    public void CustomEffect<T>(T data, Action<T, CustomFilterEffectContext> action, Func<T, Rect, Rect> transformBounds)
        where T : IEquatable<T>
    {
        AddItem(new FEItem_CustomEffect<T>(data, action, transformBounds));
        Bounds = transformBounds.Invoke(data, Bounds);
    }

    public void CustomEffect<T>(T data, Action<T, CustomFilterEffectContext> action)
    {
        AddItem(new FEItem_CustomEffect<T>(data, action, null));
        Bounds = Rect.Invalid;
    }

    public void Apply(FilterEffect? filterEffect)
    {
        if (filterEffect != null)
        {
            if (Bounds.IsInvalid)
            {
                if (filterEffect is { IsEnabled: true })
                {
                    _renderTimeItems.Add(filterEffect);
                }
            }
            else
            {
                var tmp = _current;
                try
                {
                    _current = filterEffect;

                    if (filterEffect is { IsEnabled: true })
                    {
                        filterEffect.ApplyTo(this);
                    }
                }
                finally
                {
                    _current = tmp;
                }
            }
        }
    }

    public int CountItems()
    {
        return _items.Count;
    }

    public void Dispose()
    {
        _items.Dispose();
        _renderTimeItems.Dispose();
    }
}
