using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics;

public readonly struct BrushConstructor(
    Rect bounds, Brush.Resource? brush, BlendMode blendMode, float scale = 1f,
    float maxWorkingScale = float.PositiveInfinity)
{
    private static readonly ILogger s_logger = Log.CreateLogger("BrushConstructor");
    private readonly BrushTileContent? _tileContent;

    internal BrushConstructor(
        Rect bounds,
        ResolvedBrush brush,
        BlendMode blendMode,
        float scale = 1f,
        float maxWorkingScale = float.PositiveInfinity)
        : this(bounds, brush.Resource, blendMode, scale, maxWorkingScale)
    {
        _tileContent = brush.TileContent;
    }

    public Rect Bounds { get; } = bounds;

    public Brush.Resource? Brush { get; } = brush;

    public BlendMode BlendMode { get; } = blendMode;

    /// <summary>
    /// Render density (device px per logical unit) of the canvas this brush fills into.
    /// Tile/image brushes rasterize intermediates at <c>ceil(size * Scale)</c> and compensate the shader matrix.
    /// </summary>
    public float Scale { get; } = scale;

    /// <summary>Working-scale ceiling forwarded into nested pulls (e.g. <see cref="DrawableBrush"/>).</summary>
    public float MaxWorkingScale { get; } = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);

    public void ConfigurePaint(SKPaint paint)
    {
        // Handle BrushPresenter by delegating to the target brush
        if (Brush is BrushPresenter.Resource presenter && presenter.Target != null)
        {
            new BrushConstructor(Bounds, presenter.Target, BlendMode, Scale, MaxWorkingScale).ConfigurePaint(paint);
            return;
        }

        float opacity = (Brush?.Opacity ?? 0) / 100f;
        paint.IsAntialias = true;
        paint.BlendMode = (SKBlendMode)BlendMode;

        paint.Color = new SKColor(255, 255, 255, (byte)(255 * opacity));

        if (Brush is SolidColorBrush.Resource solid)
        {
            paint.Color = new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * opacity));
        }
        else if (Brush is GradientBrush.Resource gradient)
        {
            ConfigureGradientBrush(paint, gradient);
        }
        else if (Brush is TileBrush.Resource tileBrush)
        {
            ConfigureTileBrush(paint, tileBrush);
        }
        else if (Brush is PerlinNoiseBrush.Resource perlinNoiseBrush)
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
        // Handle BrushPresenter by delegating to the target brush
        if (Brush is BrushPresenter.Resource presenter && presenter.Target != null)
        {
            return new BrushConstructor(Bounds, presenter.Target, BlendMode, Scale, MaxWorkingScale).CreateShader();
        }

        float opacity = (Brush?.Opacity ?? 0) / 100f;
        if (Brush is SolidColorBrush.Resource solid)
        {
            return SKShader.CreateColor(new SKColor(solid.Color.R, solid.Color.G, solid.Color.B,
                (byte)(solid.Color.A * opacity)));
        }
        else if (Brush is GradientBrush.Resource gradient)
        {
            return CreateGradientShader(gradient);
        }
        else if (Brush is TileBrush.Resource tileBrush)
        {
            return CreateTileShader(tileBrush);
        }
        else if (Brush is PerlinNoiseBrush.Resource perlinNoiseBrush)
        {
            return CreatePerlinNoiseShader(perlinNoiseBrush);
        }

        return null;
    }

    private SKShader? CreateGradientShader(GradientBrush.Resource gradientBrush)
    {
        var tileMode = gradientBrush.SpreadMethod.ToSKShaderTileMode();
        SKColor[] stopColors = gradientBrush.GradientStops.Select(s => s.Color.ToSKColor()).ToArray();
        float[] stopOffsets = gradientBrush.GradientStops.Select(s => s.Offset).ToArray();

        switch (gradientBrush)
        {
            case LinearGradientBrush.Resource linearGradient:
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
                        Point transformOrigin = linearGradient.TransformOrigin.ToPixels(Bounds.Size);
                        var offset = Matrix.CreateTranslation(transformOrigin + Bounds.Position);
                        Matrix transform = (-offset) * linearGradient.Transform.Matrix * offset;

                        return SKShader.CreateLinearGradient(
                            start, end, stopColors, stopOffsets, tileMode, transform.ToSKMatrix());
                    }
                }
            case RadialGradientBrush.Resource radialGradient:
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
                            Point transformOrigin = radialGradient.TransformOrigin.ToPixels(Bounds.Size);
                            var offset = Matrix.CreateTranslation(transformOrigin + Bounds.Position);
                            Matrix transform = (-offset) * radialGradient.Transform.Matrix * (offset);

                            return SKShader.CreateRadialGradient(
                                center, radius, stopColors, stopOffsets, tileMode, transform.ToSKMatrix());
                        }
                    }
                    else
                    {
                        // when the origin is different to the center use a two point ConicalGradient to match the behaviour of D2D

                        // reverse the order of the stops to match D2D
                        var reversedColors = new SKColor[stopColors.Length];
                        Array.Copy(stopColors, reversedColors, stopColors.Length);
                        Array.Reverse(reversedColors);

                        // and then reverse the reference point of the stops
                        float[] reversedStops = new float[stopOffsets.Length];
                        for (int i = 0; i < stopOffsets.Length; i++)
                        {
                            reversedStops[i] = stopOffsets[i];
                            if (reversedStops[i] > 0 && reversedStops[i] < 1)
                            {
                                reversedStops[i] = Math.Abs(1 - stopOffsets[i]);
                            }
                        }

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
                            Point transformOrigin = radialGradient.TransformOrigin.ToPixels(Bounds.Size);
                            var offset = Matrix.CreateTranslation(transformOrigin + Bounds.Position);
                            Matrix transform = (-offset) * radialGradient.Transform.Matrix * (offset);

                            return SKShader.CreateCompose(
                                SKShader.CreateColor(reversedColors[0]),
                                SKShader.CreateTwoPointConicalGradient(
                                    center, radius, origin, 0, reversedColors,
                                    reversedStops, tileMode, transform.ToSKMatrix()));
                        }
                    }
                }
            case ConicGradientBrush.Resource conicGradient:
                {
                    var center = conicGradient.Center.ToPixels(Bounds.Size).ToSKPoint();
                    center.Offset(Bounds.X, Bounds.Y);

                    // Skia's default is that angle 0 is from the right hand side of the center point
                    // but we are matching CSS where the vertical point above the center is 0.
                    float angle = conicGradient.Angle - 90;
                    var rotation = SKMatrix.CreateRotationDegrees(angle, center.X, center.Y);

                    if (conicGradient.Transform is not null)
                    {
                        Point transformOrigin = conicGradient.TransformOrigin.ToPixels(Bounds.Size);
                        var offset = Matrix.CreateTranslation(transformOrigin + Bounds.Position);
                        Matrix transform = (-offset) * conicGradient.Transform.Matrix * (offset);

                        rotation = rotation.PreConcat(transform.ToSKMatrix());
                    }

                    return SKShader.CreateSweepGradient(center, stopColors, stopOffsets, rotation);
                }
        }

        return null;
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
        if (tileBrush is DrawableBrush.Resource)
        {
            return _tileContent is { } content
                ? CreateDrawableTileShader(tileBrush, content)
                : null;
        }

        float s = Scale;
        SKImage? skImage;
        PixelSize pixelSize;     // logical content size (drives TileBrushCalculator)
        float contentDensity;    // skImage device px per logical content unit

        if (tileBrush is ImageBrush.Resource imageBrush
            && imageBrush.Source?.Bitmap is { } bitmap)
        {
            skImage = SKImage.FromBitmap(bitmap.SKBitmap);
            pixelSize = new(bitmap.Width, bitmap.Height);
            contentDensity = 1f; // the bitmap's native pixels ARE the logical content (1:1)
        }
        else
        {
            throw new InvalidOperationException($"'{tileBrush.GetType().Name}' not supported.");
        }

        RenderTarget? intermediate = null;
        try
        {
            if (skImage == null) return null;

            var calc = new TileBrushCalculator(tileBrush, pixelSize.ToSize(1), Bounds.Size);

            int iw = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Width * s));
            int ih = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Height * s));

            intermediate = RenderTarget.Create(iw, ih);
            if (intermediate == null)
            {
                s_logger.LogWarning(
                    "Tile-brush intermediate allocation failed ({Width}x{Height} px, density {Scale}); preview fill degrades to solid white, delivery render fails fast.",
                    iw, ih, s);
                ThrowIfDeliveryAllocationFailure(
                    $"Tile-brush intermediate allocation failed ({iw}x{ih} px, density {s}).");
                return null;
            }

            // Density 1: the SetMatrix below builds an absolute device matrix with Scale(s) folded in.
            using (var canvas = new ImmediateCanvas(intermediate, 1f, MaxWorkingScale))
            using (var paintTmp = new SKPaint())
            {
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

            SKMatrix tileTransform = tileBrush.TileMode != TileMode.None
                ? SKMatrix.CreateTranslation(-calc.DestinationRect.X, -calc.DestinationRect.Y)
                : SKMatrix.CreateIdentity();

            SKShaderTileMode tileX = tileBrush.TileMode == TileMode.None
                ? SKShaderTileMode.Decal
                : tileBrush.TileMode == TileMode.FlipX || tileBrush.TileMode == TileMode.FlipXY
                    ? SKShaderTileMode.Mirror
                    : SKShaderTileMode.Repeat;

            SKShaderTileMode tileY = tileBrush.TileMode == TileMode.None
                ? SKShaderTileMode.Decal
                : tileBrush.TileMode == TileMode.FlipY || tileBrush.TileMode == TileMode.FlipXY
                    ? SKShaderTileMode.Mirror
                    : SKShaderTileMode.Repeat;


            if (tileBrush.Transform is not null)
            {
                Point origin = tileBrush.TransformOrigin.ToPixels(Bounds.Size);
                var offset = Matrix.CreateTranslation(origin + Bounds.Position);
                Matrix transform = (-offset) * tileBrush.Transform.Matrix * offset;

                tileTransform = tileTransform.PreConcat(transform.ToSKMatrix());
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
            intermediate?.Dispose();
        }
    }

    private SKShader? CreateDrawableTileShader(
        TileBrush.Resource tileBrush,
        BrushTileContent content)
    {
        float s = Scale;
        var calc = new TileBrushCalculator(tileBrush, content.Bounds.Size, Bounds.Size);
        int iw = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Width * s));
        int ih = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Height * s));
        RenderTarget? intermediate = RenderTarget.Create(iw, ih);
        if (intermediate is null)
        {
            s_logger.LogWarning(
                "Drawable-brush intermediate allocation failed ({Width}x{Height} px, density {Scale}); preview fill degrades to transparent, delivery render fails fast.",
                iw,
                ih,
                s);
            ThrowIfDeliveryAllocationFailure(
                $"Drawable-brush intermediate allocation failed ({iw}x{ih} px, density {s}).");
            return null;
        }

        try
        {
            using (var canvas = new ImmediateCanvas(intermediate, 1f, MaxWorkingScale))
            using (var paint = new SKPaint { Shader = content.Shader })
            {
                canvas.Canvas.Clear();
                canvas.Canvas.Save();
                Rect clip = calc.IntermediateClip;
                canvas.Canvas.ClipRect(new SKRect(
                    (float)clip.Left * s,
                    (float)clip.Top * s,
                    (float)clip.Right * s,
                    (float)clip.Bottom * s));
                SKMatrix draw = SKMatrix.CreateScale(s, s)
                    .PreConcat(calc.IntermediateTransform.ToSKMatrix())
                    .PreConcat(SKMatrix.CreateTranslation(
                        -(float)content.Bounds.X,
                        -(float)content.Bounds.Y));
                canvas.Canvas.SetMatrix(draw);
                canvas.Canvas.DrawRect(content.Bounds.ToSKRect(), paint);
                canvas.Canvas.Restore();
            }

            (SKShaderTileMode tileX, SKShaderTileMode tileY) = ResolveTileModes(tileBrush.TileMode);
            SKMatrix tileTransform = CreateTileTransform(tileBrush, calc, s);
            using SKImage snapshot = intermediate.Value.Snapshot();
            using SKImage raster = snapshot.ToRasterImage();
            return raster.ToShader(tileX, tileY, tileTransform);
        }
        finally
        {
            intermediate.Dispose();
        }
    }

    private (SKShaderTileMode X, SKShaderTileMode Y) ResolveTileModes(TileMode tileMode)
    {
        SKShaderTileMode x = tileMode == TileMode.None
            ? SKShaderTileMode.Decal
            : tileMode is TileMode.FlipX or TileMode.FlipXY
                ? SKShaderTileMode.Mirror
                : SKShaderTileMode.Repeat;
        SKShaderTileMode y = tileMode == TileMode.None
            ? SKShaderTileMode.Decal
            : tileMode is TileMode.FlipY or TileMode.FlipXY
                ? SKShaderTileMode.Mirror
                : SKShaderTileMode.Repeat;
        return (x, y);
    }

    private SKMatrix CreateTileTransform(
        TileBrush.Resource tileBrush,
        TileBrushCalculator calc,
        float scale)
    {
        SKMatrix tileTransform = tileBrush.TileMode != TileMode.None
            ? SKMatrix.CreateTranslation(-calc.DestinationRect.X, -calc.DestinationRect.Y)
            : SKMatrix.CreateIdentity();

        if (tileBrush.Transform is not null)
        {
            Point origin = tileBrush.TransformOrigin.ToPixels(Bounds.Size);
            var offset = Matrix.CreateTranslation(origin + Bounds.Position);
            Matrix transform = (-offset) * tileBrush.Transform.Matrix * offset;
            tileTransform = tileTransform.PreConcat(transform.ToSKMatrix());
        }

        return tileTransform.PreConcat(SKMatrix.CreateScale(1f / scale, 1f / scale));
    }

    private void ThrowIfDeliveryAllocationFailure(string message)
    {
        if (float.IsPositiveInfinity(MaxWorkingScale))
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

        Point transformOrigin = perlinNoiseBrush.TransformOrigin.ToPixels(Bounds.Size);
        var offset = Matrix.CreateTranslation(transformOrigin + Bounds.Position);
        Matrix transform = (-offset) * perlinNoiseBrush.Transform.Matrix * offset;

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
        SKShader? shader = CreatePerlinNoiseShader(perlinNoiseBrush);
        if (shader != null)
        {
            paint.Shader = shader;
        }
    }
}
