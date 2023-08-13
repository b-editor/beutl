using System.ComponentModel;
using System.Reactive;
using System.Runtime.InteropServices;

using Beutl.Collections.Pooled;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectContext : IDisposable, IEquatable<FilterEffectContext>
{
    private readonly PooledList<(FilterEffect FE, int Version)> _versions;
    internal readonly PooledList<IFEItem> _items;

    public FilterEffectContext(Rect bounds)
    {
        Bounds = OriginalBounds = bounds;
        _versions = new PooledList<(FilterEffect, int)>(ClearMode.Always);
        _items = new PooledList<IFEItem>();
    }

    private FilterEffectContext(FilterEffectContext obj)
    {
        OriginalBounds = obj.OriginalBounds;
        Bounds = obj.Bounds;
        _versions = new PooledList<(FilterEffect, int)>(obj._versions.Span, ClearMode.Always);
        _items = new PooledList<IFEItem>(obj._items);
    }

    public Rect Bounds { get; private set; }

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

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSkiaFilter<T>(T data, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> factory, Func<T, Rect, Rect> transformBounds)
        where T : IEquatable<T>
    {
        _items.Add(new FEItem_Skia<T>(data, factory, transformBounds));
        Bounds = transformBounds.Invoke(data, Bounds);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendSKColorFilter<T>(T data, Func<T, FilterEffectActivator, SKColorFilter?> factory)
        where T : IEquatable<T>
    {
        _items.Add(new FEItem_SKColorFilter<T>(data, factory));
    }

    public void DropShadowOnly(Point position, Vector sigma, Color color)
    {
        AppendSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input, _) => SKImageFilter.CreateDropShadowOnly(t.position.X, t.position.Y, t.sigma.X, t.sigma.Y, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.X * 3, t.sigma.Y * 3)));
    }

    public void DropShadow(Point position, Vector sigma, Color color)
    {
        AppendSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input, _) => SKImageFilter.CreateDropShadow(t.position.X, t.position.Y, t.sigma.X, t.sigma.Y, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds.Union(bounds
                .Translate(t.position)
                .Inflate(new Thickness(t.sigma.X * 3, t.sigma.Y * 3))));
    }

    public void Blur(Vector sigma)
    {
        if (sigma.X < 0)
            sigma = sigma.WithX(0);
        if (sigma.Y < 0)
            sigma = sigma.WithY(0);

        AppendSkiaFilter(
            data: sigma,
            factory: static (sigma, input, _) =>
            {
                if (sigma.X == 0 && sigma.Y == 0)
                    return null;

                return SKImageFilter.CreateBlur(sigma.X, sigma.Y, input);
            },
            transformBounds: static (sigma, bounds) => bounds.Inflate(new Thickness(sigma.X * 3, sigma.Y * 3)));
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
    public void InnerShadow(Point position, Vector sigma, Color color)
    {
        AppendSkiaFilter(
            data: (position, sigma, color),
            factory: static (data, input, activator) =>
            {
                using var dst = SKColorFilter.CreateBlendMode(SKColors.Black, SKBlendMode.Dst);
                using var sourceGraphic = SKImageFilter.CreateColorFilter(dst);

                using var srcIn = SKColorFilter.CreateBlendMode(SKColors.Black, SKBlendMode.SrcIn);
                using var sourceAlpha = SKImageFilter.CreateColorFilter(srcIn);

                using var srcOut = SKColorFilter.CreateBlendMode(data.color.ToSKColor(), SKBlendMode.SrcOut);
                using var f1 = SKImageFilter.CreateColorFilter(srcOut);
                using var f2 = SKImageFilter.CreateOffset(data.position.X, data.position.Y, f1);
                using var f3 = SKImageFilter.CreateBlur(data.sigma.X, data.sigma.Y, SKShaderTileMode.Decal, f2);
                using var f4 = SKImageFilter.CreateBlendMode(SKBlendMode.SrcIn, sourceAlpha, f3);

                using var srcOver = SKImageFilter.CreateBlendMode(SKBlendMode.SrcOver, sourceGraphic, f4);
                return SKImageFilter.CreateCompose(srcOver, input);
            },
            transformBounds: static (_, bounds) => bounds);
    }

    public void InnerShadowOnly(Point position, Vector sigma, Color color)
    {
        AppendSkiaFilter(
            data: (position, sigma, color),
            factory: static (data, input, activator) =>
            {
                using var dst = SKColorFilter.CreateBlendMode(SKColors.Black, SKBlendMode.Dst);
                using var sourceGraphic = SKImageFilter.CreateColorFilter(dst);

                using var srcIn = SKColorFilter.CreateBlendMode(SKColors.Black, SKBlendMode.SrcIn);
                using var sourceAlpha = SKImageFilter.CreateColorFilter(srcIn);

                using var srcOut = SKColorFilter.CreateBlendMode(data.color.ToSKColor(), SKBlendMode.SrcOut);
                using var f1 = SKImageFilter.CreateColorFilter(srcOut);
                using var f2 = SKImageFilter.CreateOffset(data.position.X, data.position.Y, f1);
                using var f3 = SKImageFilter.CreateBlur(data.sigma.X, data.sigma.Y, SKShaderTileMode.Decal, f2);
                return SKImageFilter.CreateBlendMode(SKBlendMode.SrcIn, sourceAlpha, f3);
            },
            transformBounds: static (_, bounds) => bounds);
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
        AppendSKColorFilter(matrix, (m, _) => SKColorFilter.CreateColorMatrix(m.ToArrayForSkia()));
    }

    public void ColorMatrix<T>(T data, Func<T, ColorMatrix> factory)
        where T : IEquatable<T>
    {
        AppendSKColorFilter(
            (data, factory),
            (t, _) => SKColorFilter.CreateColorMatrix((t.factory.Invoke(t.data)).ToArrayForSkia()));
    }

    public void Saturate(float amount)
    {
        AppendSKColorFilter(amount, (s, _) =>
        {
            float[] array = new float[20];
            Graphics.ColorMatrix.CreateSaturateMatrix(s, array);
            //M15,M25,M35,M45がゼロなので意味がない
            //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

            return SKColorFilter.CreateColorMatrix(array);
        });
    }

    public void HueRotate(float degrees)
    {
        AppendSKColorFilter(degrees, (s, _) =>
        {
            float[] array = new float[20];
            Graphics.ColorMatrix.CreateHueRotateMatrix(degrees, array);
            //M15,M25,M35,M45がゼロなので意味がない
            //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

            return SKColorFilter.CreateColorMatrix(array);
        });
    }

    public void LuminanceToAlpha()
    {
        AppendSKColorFilter(Unit.Default, (_, _) =>
        {
            float[] array = new float[20];
            Graphics.ColorMatrix.CreateLuminanceToAlphaMatrix(array);
            //M15,M25,M35,M45がゼロなので意味がない
            //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

            return SKColorFilter.CreateColorMatrix(array);
        });
    }

    public void Brightness(float amount)
    {
        AppendSKColorFilter(amount, (s, _) =>
        {
            float[] array = new float[20];
            Graphics.ColorMatrix.CreateBrightness(amount, array);
            //M15,M25,M35,M45がゼロなので意味がない
            //Graphics.ColorMatrix.ToSkiaColorMatrix(array);

            return SKColorFilter.CreateColorMatrix(array);
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

    public void LookupTable(LookupTable table, float strength = 1, int versionCounter = -1)
    {
        AppendSKColorFilter((table, strength, versionCounter), (data, _) =>
        {
            if (data.table.IsDisposed)
                return null;

            if (data.table.Dimension == LookupTableDimension.OneDimension)
            {
                return SKColorFilter.CreateTable(data.table.ToByteArray(data.strength, 0));
            }
            else
            {
                return SKColorFilter.CreateTable(
                    Graphics.LookupTable.s_linear,
                    data.table.ToByteArray(data.strength, 0),
                    data.table.ToByteArray(data.strength, 1),
                    data.table.ToByteArray(data.strength, 2));
            }
        });
    }

    public void LookupTable<T>(T data, Func<T, LookupTable> factory, float strength = 1)
        where T : IEquatable<T>
    {
        AppendSKColorFilter((data, factory, strength), (data, _) =>
        {
            using (LookupTable table = data.factory.Invoke(data.data))
            {
                if (table.Dimension == LookupTableDimension.OneDimension)
                {
                    byte[] array = table.ToByteArray(data.strength, 0);
                    return SKColorFilter.CreateTable(
                        Graphics.LookupTable.s_linear,
                        array,
                        array,
                        array);
                }
                else
                {
                    return SKColorFilter.CreateTable(
                        Graphics.LookupTable.s_linear,
                        table.ToByteArray(data.strength, 0),
                        table.ToByteArray(data.strength, 1),
                        table.ToByteArray(data.strength, 2));
                }
            }
        });
    }

    public void Custom<T>(T data, Action<T, FilterEffectCustomOperationContext> action, Func<T, Rect, Rect> transformBounds)
        where T : IEquatable<T>
    {
        _items.Add(new FEItem_Custom<T>(data, action, transformBounds));
        Bounds = transformBounds.Invoke(data, Bounds);
    }

    public void Apply(FilterEffect? filterEffect)
    {
        if (filterEffect != null)
        {
            _versions.Add((filterEffect, filterEffect.Version));

            if (filterEffect is { IsEnabled: true })
            {
                filterEffect.ApplyTo(this);
            }
        }
    }

    public int FirstVersion()
    {
        if (_versions.Count == 0)
            throw new InvalidOperationException("有効なエフェクトバージョンがありません");

        return _versions[0].Version;
    }

    public int CountItems()
    {
        return _items.Count;
    }

    public Rect TransformBounds(Range range)
    {
        Rect rect = Bounds;
        foreach (IFEItem item in _items.Span[range])
        {
            rect = item.TransformBounds(rect);
        }

        return rect;
    }

    public int CountEquals(FilterEffectContext? other)
    {
        if (other == null)
            return 0;
        //if (other.OriginalBounds != OriginalBounds)
        //    return -1;

        int minLength = Math.Min(other._items.Count, _items.Count);
        for (int i = 0; i < minLength; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return i;
            }
        }

        return 0;
    }

    public bool Equals(FilterEffectContext? other)
    {
        if (other == null
            || _items.Count != other._items.Count
            || Bounds != other.Bounds)
            return false;

        Span<IFEItem> items = _items.Span;
        Span<IFEItem> otherItems = other._items.Span;
        return items.SequenceEqual(otherItems);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as FilterEffectContext);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (IFEItem? item in _items.Span)
        {
            hash.Add(item.GetHashCode());
        }

        return hash.ToHashCode();
    }

    public void Dispose()
    {
        _versions.Dispose();
        _items.Dispose();
    }
}
