using System.Reactive;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class FilterEffectContext
{
    public void DropShadowOnly(Point position, Size sigma, Color color)
    {
        AppendDirectSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input) => SKImageFilter.CreateDropShadowOnly(t.position.X, t.position.Y,
                t.sigma.Width, t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds
                .Translate(t.position)
                .Inflate(BlurExtent(t.sigma)),
            transformSamplingBounds: static (t, region) => region
                .Translate(-t.position)
                .Inflate(BlurExtent(t.sigma)));
    }

    public void DropShadow(Point position, Size sigma, Color color)
    {
        AppendDirectSkiaFilter(
            data: (position, sigma, color),
            factory: static (t, input) => SKImageFilter.CreateDropShadow(t.position.X, t.position.Y, t.sigma.Width,
                t.sigma.Height, t.color.ToSKColor(), input),
            transformBounds: static (t, bounds) => bounds.Union(bounds
                .Translate(t.position)
                .Inflate(BlurExtent(t.sigma))),
            transformSamplingBounds: static (t, region) => region.Union(region
                .Translate(-t.position)
                .Inflate(BlurExtent(t.sigma))));
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
                bounds.Inflate(BlurExtent(sigma)),
            transformSamplingBounds: static (sigma, region) =>
                region.Inflate(BlurExtent(sigma)));
    }

    /// <summary>How far a Gaussian blur reaches past its input: three sigmas, beyond which the kernel is negligible.</summary>
    private static Thickness BlurExtent(Size sigma) => new(sigma.Width * 3, sigma.Height * 3);

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
                    w - data.kernelOffset.X,
                    h - data.kernelOffset.Y,
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

    /// <summary>
    /// Keeps the part of the output inside <paramref name="rect"/> and fills the rest according to
    /// <paramref name="spreadMethod"/>.
    /// </summary>
    /// <param name="rect">The area to keep, in the coordinates of <see cref="Bounds"/>.</param>
    /// <param name="spreadMethod">
    /// <see cref="GradientSpreadMethod.Decal"/> leaves everything outside <paramref name="rect"/> transparent and
    /// shrinks <see cref="Bounds"/> to the crop. <see cref="GradientSpreadMethod.Pad"/> extends the crop's edge
    /// pixels, <see cref="GradientSpreadMethod.Repeat"/> tiles the crop and <see cref="GradientSpreadMethod.Reflect"/>
    /// mirrors it, across the bounds the output already had.
    /// </param>
    public void Crop(Rect rect, GradientSpreadMethod spreadMethod = GradientSpreadMethod.Decal)
    {
        rect = rect.Normalize();
        AppendDirectSkiaFilter(
            data: (rect, spreadMethod),
            factory: static (data, input) => data.rect.Width > 0 && data.rect.Height > 0
                ? SKImageFilter.CreateCrop(data.rect.ToSKRect(), data.spreadMethod.ToSKShaderTileMode(), input)
                // A factory that returns null passes its input through, so a crop to nothing needs a filter
                // that draws nothing.
                : SKImageFilter.CreateEmpty(),
            transformBounds: static (data, bounds) =>
            {
                if (data.rect.Width <= 0 || data.rect.Height <= 0)
                    return Rect.Empty;

                // Every mode but decal fills the whole plane, so only the area the output already covered bounds it.
                return data.spreadMethod == GradientSpreadMethod.Decal ? bounds.Intersect(data.rect) : bounds;
            },
            // Outside the crop, a tile anywhere in the output reads from anywhere in the crop.
            transformSamplingBounds: static (data, region) =>
                data.spreadMethod == GradientSpreadMethod.Decal ? region.Intersect(data.rect) : data.rect);
    }

    public void ColorMatrix(in ColorMatrix matrix)
    {
        if (matrix.IsIdentity)
            return;

        AppendSKColorFilter(matrix, (m, _) =>
        {
            using var lease = PooledColorMatrix.Rent(out float[] array);
            m.ToArrayForSkia(array);
            return SKColorFilter.CreateColorMatrix(array);
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
        using var lease = PooledColorMatrix.Rent(out float[] array);
        Graphics.ColorMatrix.CreateSaturateMatrix(amount, array);

        ShaderColorMatrix(array);
    }

    public void HueRotate(float degrees)
    {
        using var lease = PooledColorMatrix.Rent(out float[] array);
        Graphics.ColorMatrix.CreateHueRotateMatrix(degrees, array);

        ShaderColorMatrix(array);
    }

    public void LuminanceToAlpha()
    {
        AppendSKColorFilter(Unit.Default, (_, _) =>
        {
            using var lease = PooledColorMatrix.Rent(out float[] array);
            Graphics.ColorMatrix.CreateLuminanceToAlphaMatrix(array);

            return SKColorFilter.CreateColorMatrix(array);
        });
    }

    public void Brightness(float amount)
    {
        // Recorded as a CurrentPixel shader stage rather than a Skia color filter so that an adjacent shader
        // stage can fuse with it instead of splitting the chain at a effect-item segment.
        using var lease = PooledColorMatrix.Rent(out float[] array);
        Graphics.ColorMatrix.CreateBrightness(amount, array);

        ShaderColorMatrix(array);
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

        using var lease = PooledColorMatrix.Rent(out float[] array);
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
}
