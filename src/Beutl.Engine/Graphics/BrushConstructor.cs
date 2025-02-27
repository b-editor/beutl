using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics;

public readonly struct BrushConstructor(Rect bounds, IBrush? brush, BlendMode blendMode)
{
    public Rect Bounds { get; } = bounds;

    public IBrush? Brush { get; } = brush;

    public BlendMode BlendMode { get; } = blendMode;

    public void ConfigurePaint(SKPaint paint)
    {
        float opacity = (Brush?.Opacity ?? 0) / 100f;
        paint.IsAntialias = true;
        paint.BlendMode = (SKBlendMode)BlendMode;

        paint.Color = new SKColor(255, 255, 255, (byte)(255 * opacity));

        if (Brush is ISolidColorBrush solid)
        {
            paint.Color = new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * opacity));
        }
        else if (Brush is IGradientBrush gradient)
        {
            ConfigureGradientBrush(paint, gradient);
        }
        else if (Brush is ITileBrush tileBrush)
        {
            ConfigureTileBrush(paint, tileBrush);
        }
        else if (Brush is IPerlinNoiseBrush perlinNoiseBrush)
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
        float opacity = (Brush?.Opacity ?? 0) / 100f;
        if (Brush is ISolidColorBrush solid)
        {
            return SKShader.CreateColor(new SKColor(solid.Color.R, solid.Color.G, solid.Color.B,
                (byte)(solid.Color.A * opacity)));
        }
        else if (Brush is IGradientBrush gradient)
        {
            return CreateGradientShader(gradient);
        }
        else if (Brush is ITileBrush tileBrush)
        {
            return CreateTileShader(tileBrush);
        }
        else if (Brush is IPerlinNoiseBrush perlinNoiseBrush)
        {
            return CreatePerlinNoiseShader(perlinNoiseBrush);
        }

        return null;
    }

    private SKShader? CreateGradientShader(IGradientBrush gradientBrush)
    {
        var tileMode = gradientBrush.SpreadMethod.ToSKShaderTileMode();
        SKColor[] stopColors = gradientBrush.GradientStops.Select(s => s.Color.ToSKColor()).ToArray();
        float[] stopOffsets = gradientBrush.GradientStops.Select(s => s.Offset).ToArray();

        switch (gradientBrush)
        {
            case ILinearGradientBrush linearGradient:
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
                        Matrix transform = (-offset) * linearGradient.Transform.Value * offset;

                        return SKShader.CreateLinearGradient(
                            start, end, stopColors, stopOffsets, tileMode, transform.ToSKMatrix());
                    }
                }
            case IRadialGradientBrush radialGradient:
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
                            Matrix transform = (-offset) * radialGradient.Transform.Value * (offset);

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
                            Matrix transform = (-offset) * radialGradient.Transform.Value * (offset);

                            return SKShader.CreateCompose(
                                SKShader.CreateColor(reversedColors[0]),
                                SKShader.CreateTwoPointConicalGradient(
                                    center, radius, origin, 0, reversedColors,
                                    reversedStops, tileMode, transform.ToSKMatrix()));
                        }
                    }
                }
            case IConicGradientBrush conicGradient:
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
                        Matrix transform = (-offset) * conicGradient.Transform.Value * (offset);

                        rotation = rotation.PreConcat(transform.ToSKMatrix());
                    }

                    return SKShader.CreateSweepGradient(center, stopColors, stopOffsets, rotation);
                }
        }

        return null;
    }

    private void ConfigureGradientBrush(SKPaint paint, IGradientBrush gradientBrush)
    {
        using var shader = CreateGradientShader(gradientBrush);
        if (shader != null)
        {
            paint.Shader = shader;
        }
    }

    private SKShader? CreateTileShader(ITileBrush tileBrush)
    {
        RenderTarget? renderTarget = null;
        SKImage? skImage;
        PixelSize pixelSize;

        if (tileBrush is RenderSceneBrush sceneBrush)
        {
            RenderScene? scene = sceneBrush.Scene;
            if (scene != null)
            {
                renderTarget = RenderTarget.Create(scene.Size.Width, scene.Size.Height);
                if (renderTarget == null) return null;

                using (var icanvas = new ImmediateCanvas(renderTarget))
                using (icanvas.PushTransform(Matrix.CreateTranslation(-sceneBrush.Bounds.X, -sceneBrush.Bounds.Y)))
                {
                    scene.Render(icanvas);
                }

                pixelSize = scene.Size;
                skImage = renderTarget.Value.Snapshot();
            }
            else
            {
                return null;
            }
        }
        else if (tileBrush is IImageBrush imageBrush
                 && imageBrush.Source?.TryGetRef(out Ref<IBitmap>? bitmap) == true)
        {
            using (bitmap)
            {
                skImage = bitmap.Value.ToSKImage();
                pixelSize = new(bitmap.Value.Width, bitmap.Value.Height);
            }
        }
        else if (tileBrush is IDrawableBrush drawableBrush)
        {
            if (drawableBrush.Drawable is null) return null;
            renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height);
            if (renderTarget == null) return null;

            using (var icanvas = new ImmediateCanvas(renderTarget))
            {
                icanvas.DrawDrawable(drawableBrush.Drawable);
            }

            pixelSize = new PixelSize(renderTarget.Width, renderTarget.Height);
            skImage = renderTarget.Value.Snapshot();
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
            SKSizeI intermediateSize = calc.IntermediateSize.ToSKSize().ToSizeI();

            intermediate = RenderTarget.Create(intermediateSize.Width, intermediateSize.Height);
            if (intermediate == null) return null;

            SKCanvas canvas = intermediate.Value.Canvas;
            using (var paintTmp = new SKPaint())
            {
                canvas.Clear();
                canvas.Save();
                canvas.ClipRect(calc.IntermediateClip.ToSKRect());
                canvas.SetMatrix(calc.IntermediateTransform.ToSKMatrix());

                canvas.DrawImage(skImage, 0, 0, tileBrush.BitmapInterpolationMode.ToSKSamplingOptions(), paintTmp);

                canvas.Restore();
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
                Matrix transform = (-offset) * tileBrush.Transform.Value * offset;

                tileTransform = tileTransform.PreConcat(transform.ToSKMatrix());
            }

            using (SKImage snapshot = intermediate.Value.Snapshot())
            {
                return snapshot.ToShader(tileX, tileY, tileTransform);
            }
        }
        finally
        {
            skImage?.Dispose();
            intermediate?.Dispose();
            renderTarget?.Dispose();
        }
    }

    private void ConfigureTileBrush(SKPaint paint, ITileBrush tileBrush)
    {
        using var shader = CreateTileShader(tileBrush);
        if (shader != null)
        {
            paint.Shader = shader;
        }
    }

    private SKShader? CreatePerlinNoiseShader(IPerlinNoiseBrush perlinNoiseBrush)
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
        Matrix transform = (-offset) * perlinNoiseBrush.Transform.Value * offset;

        if (!transform.IsIdentity)
        {
            SKShader tmp = shader;
            shader = shader.WithLocalMatrix(transform.ToSKMatrix());
            tmp.Dispose();
        }

        return shader;
    }

    private void ConfigurePerlinNoiseBrush(SKPaint paint, IPerlinNoiseBrush perlinNoiseBrush)
    {
        SKShader? shader = CreatePerlinNoiseShader(perlinNoiseBrush);
        if (shader != null)
        {
            paint.Shader = shader;
        }
    }
}
