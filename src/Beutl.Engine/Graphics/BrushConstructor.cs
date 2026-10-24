using Beutl.Animation;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics;

/// <remarks>
/// <paramref name="intent"/> and <paramref name="drawableBrushMaterializer"/> are stated rather than defaulted,
/// because either one left implicit turns a failure into transparent missing pixels.
/// </remarks>
public readonly struct BrushConstructor(
    Rect bounds, Brush.Resource? brush, BlendMode blendMode, RenderIntent intent,
    DrawableBrushMaterializer? drawableBrushMaterializer, float scale = 1f,
    float maxWorkingScale = float.PositiveInfinity)
{
    private static readonly ILogger s_logger = Log.CreateLogger("BrushConstructor");
    private readonly DrawableBrushMaterializer? _drawableBrushMaterializer = drawableBrushMaterializer;
    private readonly RenderTargetLeaseSession? _renderTargetLeaseSession;

    /// <summary>
    /// Binds the constructor to the render pass's lease session so a tile-brush intermediate is allocated
    /// through the caller's <see cref="IRenderTargetFactory"/> rather than the global allocator.
    /// </summary>
    internal BrushConstructor(
        Rect bounds,
        Brush.Resource? brush,
        BlendMode blendMode,
        float scale,
        float maxWorkingScale,
        RenderIntent intent,
        DrawableBrushMaterializer? drawableBrushMaterializer,
        RenderTargetLeaseSession? renderTargetLeaseSession)
        : this(bounds, brush, blendMode, intent, drawableBrushMaterializer, scale, maxWorkingScale)
    {
        _renderTargetLeaseSession = renderTargetLeaseSession;
    }

    public Rect Bounds { get; } = bounds;

    public Brush.Resource? Brush { get; } = brush;

    public BlendMode BlendMode { get; } = blendMode;

    /// <summary>
    /// Render density (device px per logical unit) of the canvas this brush fills into.
    /// Tile/image brushes rasterize intermediates at <c>ceil(size * Scale)</c> and compensate the shader matrix.
    /// </summary>
    public float Scale { get; } = scale;

    /// <summary>Working-scale ceiling applied to brush-owned intermediates and scoped effect-item nested pulls.</summary>
    public float MaxWorkingScale { get; } = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);

    /// <summary>
    /// Preview or delivery classification of the render this brush paints into (see <see cref="RenderIntent"/>).
    /// </summary>
    public RenderIntent Intent { get; } = Enum.IsDefined(intent)
        ? intent
        : throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown render intent.");

    public void ConfigurePaint(SKPaint paint)
    {
        Brush.Resource? brush = ResolvePresentedBrush();
        float opacity = (brush?.Opacity ?? 0) / 100f;
        paint.IsAntialias = true;
        paint.BlendMode = (SKBlendMode)BlendMode;

        paint.Color = new SKColor(255, 255, 255, (byte)(255 * opacity));

        if (brush is SolidColorBrush.Resource solid)
        {
            paint.Color = new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * opacity));
        }
        else if (brush is GradientBrush.Resource gradient)
        {
            ConfigureGradientBrush(paint, gradient);
        }
        else if (brush is TileBrush.Resource tileBrush)
        {
            ConfigureTileBrush(paint, tileBrush);
        }
        else if (brush is PerlinNoiseBrush.Resource perlinNoiseBrush)
        {
            ConfigurePerlinNoiseBrush(paint, perlinNoiseBrush);
        }
        else
        {
            paint.Color = new SKColor(255, 255, 255, 0);
        }
    }

    public SKShader? CreateShader()
    {
        Brush.Resource? brush = ResolvePresentedBrush();
        float opacity = (brush?.Opacity ?? 0) / 100f;
        if (brush is SolidColorBrush.Resource solid)
        {
            return SKShader.CreateColor(new SKColor(solid.Color.R, solid.Color.G, solid.Color.B,
                (byte)(solid.Color.A * opacity)));
        }
        else if (brush is GradientBrush.Resource gradient)
        {
            return WithOpacity(CreateGradientShader(gradient), opacity);
        }
        else if (brush is TileBrush.Resource tileBrush)
        {
            return WithOpacity(CreateTileShader(tileBrush), opacity);
        }
        else if (brush is PerlinNoiseBrush.Resource perlinNoiseBrush)
        {
            return WithOpacity(CreatePerlinNoiseShader(perlinNoiseBrush), opacity);
        }

        return null;
    }

    // ConfigurePaint scales these shaders by the paint's alpha; a bare shader is scaled by that same alpha here.
    private static SKShader? WithOpacity(SKShader? shader, float opacity)
    {
        var alpha = (byte)(255 * opacity);
        if (shader is null || alpha == 255)
            return shader;

        using (shader)
        using (var filter = SKColorFilter.CreateBlendMode(new SKColor(255, 255, 255, alpha), SKBlendMode.DstIn))
        {
            return shader.WithColorFilter(filter);
        }
    }

    private Brush.Resource? ResolvePresentedBrush()
        => BrushPresenter.Resource.ResolveTarget(Brush, "A BrushPresenter target cycle was detected.");

    private SKShader? CreateGradientShader(GradientBrush.Resource gradientBrush)
    {
        var tileMode = gradientBrush.SpreadMethod.ToSKShaderTileMode();
        SKColor[] stopColors = gradientBrush.GradientStops.Select(s => s.Color.ToSKColor()).ToArray();
        float[] stopOffsets = gradientBrush.GradientStops.Select(s => s.Offset).ToArray();

        switch (gradientBrush)
        {
            case LinearGradientBrush.Resource linearGradient:
                return CreateLinearGradientShader(linearGradient, stopColors, stopOffsets, tileMode);
            case RadialGradientBrush.Resource radialGradient:
                return CreateRadialGradientShader(radialGradient, stopColors, stopOffsets, tileMode);
            case ConicGradientBrush.Resource conicGradient:
                return CreateConicGradientShader(conicGradient, stopColors, stopOffsets);
        }

        return null;
    }

    private SKShader CreateLinearGradientShader(
        LinearGradientBrush.Resource linearGradient,
        SKColor[] stopColors,
        float[] stopOffsets,
        SKShaderTileMode tileMode)
    {
        var start = linearGradient.StartPoint.ToPixels(Bounds.Size).ToSKPoint();
        var end = linearGradient.EndPoint.ToPixels(Bounds.Size).ToSKPoint();
        start.Offset(Bounds.X, Bounds.Y);
        end.Offset(Bounds.X, Bounds.Y);

        if (linearGradient.Transform is null)
        {
            return SKShader.CreateLinearGradient(start, end, stopColors, stopOffsets, tileMode);
        }
        else
        {
            Matrix transform = CreateBrushTransform(linearGradient.TransformOrigin, linearGradient.Transform);

            return SKShader.CreateLinearGradient(
                start, end, stopColors, stopOffsets, tileMode, transform.ToSKMatrix());
        }
    }

    private SKShader CreateRadialGradientShader(
        RadialGradientBrush.Resource radialGradient,
        SKColor[] stopColors,
        float[] stopOffsets,
        SKShaderTileMode tileMode)
    {
        float radius = (radialGradient.Radius / 100) * Bounds.Width;
        var center = radialGradient.Center.ToPixels(Bounds.Size).ToSKPoint();
        var origin = radialGradient.GradientOrigin.ToPixels(Bounds.Size).ToSKPoint();
        center.Offset(Bounds.X, Bounds.Y);
        origin.Offset(Bounds.X, Bounds.Y);

        if (origin.Equals(center))
        {
            // when the origin is the same as the center the Skia RadialGradient acts the same as D2D
            if (radialGradient.Transform is null)
            {
                return SKShader.CreateRadialGradient(center, radius, stopColors, stopOffsets, tileMode);
            }
            else
            {
                Matrix transform = CreateBrushTransform(radialGradient.TransformOrigin, radialGradient.Transform);

                return SKShader.CreateRadialGradient(
                    center, radius, stopColors, stopOffsets, tileMode, transform.ToSKMatrix());
            }
        }
        else
        {
            // when the origin is different to the center use a two point ConicalGradient to match the behaviour of D2D
            ReverseStopsForConical(stopColors, stopOffsets, out SKColor[] reversedColors, out float[] reversedStops);

            // compose with a background colour of the final stop to match D2D's behaviour of filling with the final color
            if (radialGradient.Transform is null)
            {
                return SKShader.CreateCompose(
                    SKShader.CreateColor(reversedColors[0]),
                    SKShader.CreateTwoPointConicalGradient(
                        center, radius, origin, 0, reversedColors, reversedStops, tileMode));
            }
            else
            {
                Matrix transform = CreateBrushTransform(radialGradient.TransformOrigin, radialGradient.Transform);

                return SKShader.CreateCompose(
                    SKShader.CreateColor(reversedColors[0]),
                    SKShader.CreateTwoPointConicalGradient(
                        center, radius, origin, 0, reversedColors,
                        reversedStops, tileMode, transform.ToSKMatrix()));
            }
        }
    }

    /// <summary>
    /// Reverses the stops for the two-point conical gradient that stands in for a radial gradient whose origin
    /// left its centre: their order, and each inner offset measured from the other end.
    /// </summary>
    private static void ReverseStopsForConical(
        SKColor[] stopColors,
        float[] stopOffsets,
        out SKColor[] reversedColors,
        out float[] reversedStops)
    {
        // reverse the order of the stops to match D2D
        reversedColors = new SKColor[stopColors.Length];
        Array.Copy(stopColors, reversedColors, stopColors.Length);
        Array.Reverse(reversedColors);

        // and then reverse the reference point of the stops
        reversedStops = new float[stopOffsets.Length];
        for (int i = 0; i < stopOffsets.Length; i++)
        {
            reversedStops[i] = stopOffsets[i];
            if (reversedStops[i] > 0 && reversedStops[i] < 1)
            {
                reversedStops[i] = Math.Abs(1 - stopOffsets[i]);
            }
        }
    }

    private SKShader CreateConicGradientShader(
        ConicGradientBrush.Resource conicGradient,
        SKColor[] stopColors,
        float[] stopOffsets)
    {
        var center = conicGradient.Center.ToPixels(Bounds.Size).ToSKPoint();
        center.Offset(Bounds.X, Bounds.Y);

        // Skia's default is that angle 0 is from the right hand side of the center point
        // but we are matching CSS where the vertical point above the center is 0.
        float angle = conicGradient.Angle - 90;
        var rotation = SKMatrix.CreateRotationDegrees(angle, center.X, center.Y);

        if (conicGradient.Transform is not null)
        {
            Matrix transform = CreateBrushTransform(conicGradient.TransformOrigin, conicGradient.Transform);

            rotation = rotation.PreConcat(transform.ToSKMatrix());
        }

        return SKShader.CreateSweepGradient(center, stopColors, stopOffsets, rotation);
    }

    /// <summary>
    /// Builds a brush's local matrix: <paramref name="transform"/> applied about <paramref name="transformOrigin"/>,
    /// resolved within <see cref="Bounds"/>.
    /// </summary>
    private Matrix CreateBrushTransform(RelativePoint transformOrigin, Transformation.Transform.Resource transform)
    {
        Point origin = transformOrigin.ToPixels(Bounds.Size);
        var offset = Matrix.CreateTranslation(origin + Bounds.Position);
        return (-offset) * transform.Matrix * offset;
    }

    private void ConfigureGradientBrush(SKPaint paint, GradientBrush.Resource gradientBrush)
    {
        using var shader = CreateGradientShader(gradientBrush);
        if (shader != null)
        {
            paint.Shader = shader;
        }
    }

    private SKShader? CreateTileShader(TileBrush.Resource tileBrush)
    {
        float s = Scale;
        if (!TryResolveTileSource(tileBrush, s, out SKImage? skImage, out Size contentSize, out float contentDensity))
            return null;

        RenderTarget? intermediate = null;
        RenderTargetLease? intermediateLease = null;
        try
        {
            if (skImage == null) return null;

            var calc = new TileBrushCalculator(tileBrush, contentSize, Bounds.Size);

            int iw = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Width * s));
            int ih = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Height * s));

            intermediate = AcquireTileIntermediate(iw, ih, out intermediateLease);

            if (intermediate == null)
            {
                s_logger.LogWarning(
                    "Tile-brush intermediate allocation failed ({Width}x{Height} px, density {Scale}); preview fill degrades to transparent, delivery render fails fast.",
                    iw, ih, s);
                ThrowIfDeliveryContentLoss(
                    $"Tile-brush intermediate allocation failed ({iw}x{ih} px, density {s}).");
                _renderTargetLeaseSession?.MarkContentDropped();
                return null;
            }

            RasterizeTileContent(intermediate, tileBrush, in calc, skImage, s, contentDensity);

            SKMatrix tileTransform = tileBrush.TileMode != TileMode.None
                ? SKMatrix.CreateTranslation(-calc.DestinationRect.X, -calc.DestinationRect.Y)
                : SKMatrix.CreateIdentity();

            // The intermediate is zero-origin; place it in the painted geometry's local bounds
            // before applying a brush transform about those bounds.
            tileTransform = tileTransform.PostConcat(SKMatrix.CreateTranslation(Bounds.X, Bounds.Y));

            SKShaderTileMode tileX = ToShaderTileMode(tileBrush.TileMode, TileMode.FlipX);
            SKShaderTileMode tileY = ToShaderTileMode(tileBrush.TileMode, TileMode.FlipY);

            if (tileBrush.Transform is not null)
            {
                Matrix transform = CreateBrushTransform(tileBrush.TransformOrigin, tileBrush.Transform);

                tileTransform = tileTransform.PostConcat(transform.ToSKMatrix());
            }

            // Compensate the dense intermediate: Scale(1/s) un-densifies texture coords to logical.
            tileTransform = tileTransform.PreConcat(SKMatrix.CreateScale(1f / s, 1f / s));

            using (SKImage snapshot = intermediate.Value.Snapshot())
            using (SKImage raster = snapshot.ToRasterImage())
            {
                return raster.ToShader(tileX, tileY, tileTransform);
            }
        }
        finally
        {
            skImage?.Dispose();
            // A leased target belongs to the pool: release the lease, never the target behind it.
            if (intermediateLease is not null)
                intermediateLease.Dispose();
            else
                intermediate?.Dispose();
        }
    }

    /// <summary>
    /// Resolves the image a tile brush tiles at density <paramref name="s"/>, which the caller disposes, with the
    /// logical size and the device pixels per logical unit of its content.
    /// </summary>
    /// <returns><see langword="false"/> when a drawable brush produced no image; the fill degrades to transparent.</returns>
    private bool TryResolveTileSource(
        TileBrush.Resource tileBrush,
        float s,
        out SKImage? skImage,
        out Size contentSize,
        out float contentDensity)
    {
        skImage = null;
        contentSize = default;
        contentDensity = 0;
        if (tileBrush is DrawableBrush.Resource drawableBrush)
        {
            if (_drawableBrushMaterializer is not { } materializer)
            {
                s_logger.LogWarning(
                    "DrawableBrush '{Brush}' cannot be materialized because no runtime materializer is available; preview fill degrades to transparent, delivery render fails fast.",
                    drawableBrush);
                ThrowIfDeliveryContentLoss(
                    $"DrawableBrush '{drawableBrush}' cannot be materialized because the host supplied no runtime materializer.");
                return false;
            }

            if (materializer(drawableBrush, Bounds, s) is not { } materialized)
            {
                s_logger.LogWarning(
                    "The drawable-brush materializer returned no image for '{Brush}'; the fill degrades to transparent.",
                    drawableBrush);
                return false;
            }

            skImage = materialized.Image;
            contentSize = materialized.ContentBounds.Size;
            contentDensity = s;
            return true;
        }

        if (tileBrush is ImageBrush.Resource imageBrush
            && imageBrush.Source?.Bitmap is { } bitmap)
        {
            skImage = SKImage.FromBitmap(bitmap.SKBitmap);
            contentSize = new Size(bitmap.Width, bitmap.Height);
            contentDensity = 1f; // the bitmap's native pixels ARE the logical content (1:1)
            return true;
        }

        throw new InvalidOperationException($"'{tileBrush.GetType().Name}' not supported.");
    }

    /// <summary>
    /// Allocates the tile intermediate through the caller's <see cref="IRenderTargetFactory"/> when the lease session
    /// has one, and directly otherwise; <paramref name="lease"/> is the lease to release instead of the target.
    /// </summary>
    private RenderTarget? AcquireTileIntermediate(int width, int height, out RenderTargetLease? lease)
    {
        if (_renderTargetLeaseSession is { HasTargetFactory: true } leaseSession)
        {
            lease = leaseSession.TryAcquire(new PixelSize(width, height));
            return lease?.Target;
        }

        lease = null;
        return RenderTarget.Create(width, height);
    }

    private void RasterizeTileContent(
        RenderTarget intermediate,
        TileBrush.Resource tileBrush,
        in TileBrushCalculator calc,
        SKImage skImage,
        float s,
        float contentDensity)
    {
        // Density 1: the SetMatrix below builds an absolute device matrix with Scale(s) folded in.
        using (var canvas = new ImmediateCanvas(intermediate, Intent, 1f, MaxWorkingScale))
        using (var paintTmp = new SKPaint())
        {
            canvas.DrawableBrushMaterializer = _drawableBrushMaterializer;
            canvas.Canvas.Clear();
            canvas.Canvas.Save();
            Rect clip = calc.IntermediateClip;
            canvas.Canvas.ClipRect(new SKRect(
                (float)clip.Left * s, (float)clip.Top * s, (float)clip.Right * s, (float)clip.Bottom * s));
            SKMatrix draw = SKMatrix.CreateScale(s, s)
                .PreConcat(calc.IntermediateTransform.ToSKMatrix())
                .PreConcat(SKMatrix.CreateScale(1f / contentDensity, 1f / contentDensity));
            canvas.Canvas.SetMatrix(draw);

            canvas.Canvas.DrawImage(skImage, 0, 0, tileBrush.BitmapInterpolationMode.ToSKSamplingOptions(),
                paintTmp);

            canvas.Canvas.Restore();
        }
    }

    private static SKShaderTileMode ToShaderTileMode(TileMode mode, TileMode flipAxis)
        => mode == TileMode.None
            ? SKShaderTileMode.Decal
            : mode == flipAxis || mode == TileMode.FlipXY
                ? SKShaderTileMode.Mirror
                : SKShaderTileMode.Repeat;

    /// <summary>Fails the render when <see cref="Intent"/> is <see cref="RenderIntent.Delivery"/>.</summary>
    private void ThrowIfDeliveryContentLoss(string message)
    {
        if (Intent == RenderIntent.Delivery)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void ConfigureTileBrush(SKPaint paint, TileBrush.Resource tileBrush)
    {
        using var shader = CreateTileShader(tileBrush);
        if (shader != null)
        {
            paint.Shader = shader;
        }
        else
        {
            paint.Color = SKColors.Transparent;
        }
    }

    private SKShader? CreatePerlinNoiseShader(PerlinNoiseBrush.Resource perlinNoiseBrush)
    {
        SKShader? shader = perlinNoiseBrush.PerlinNoiseType switch
        {
            PerlinNoiseType.Turbulence => SKShader.CreatePerlinNoiseTurbulence(
                perlinNoiseBrush.BaseFrequencyX / 100f,
                perlinNoiseBrush.BaseFrequencyY / 100f,
                perlinNoiseBrush.Octaves,
                perlinNoiseBrush.Seed),
            PerlinNoiseType.Fractal => SKShader.CreatePerlinNoiseFractalNoise(
                perlinNoiseBrush.BaseFrequencyX / 100f,
                perlinNoiseBrush.BaseFrequencyY / 100f,
                perlinNoiseBrush.Octaves,
                perlinNoiseBrush.Seed),
            _ => null
        };

        if (shader == null) return null;

        if (perlinNoiseBrush.Transform == null) return shader;

        Matrix transform = CreateBrushTransform(perlinNoiseBrush.TransformOrigin, perlinNoiseBrush.Transform);

        if (!transform.IsIdentity)
        {
            SKShader tmp = shader;
            shader = shader.WithLocalMatrix(transform.ToSKMatrix());
            tmp.Dispose();
        }

        return shader;
    }

    private void ConfigurePerlinNoiseBrush(SKPaint paint, PerlinNoiseBrush.Resource perlinNoiseBrush)
    {
        using SKShader? shader = CreatePerlinNoiseShader(perlinNoiseBrush);
        if (shader != null)
        {
            paint.Shader = shader;
        }
    }

}
