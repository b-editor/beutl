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

    public Rect Bounds { get; } = bounds;

    public Brush.Resource? Brush { get; } = brush;

    public BlendMode BlendMode { get; } = blendMode;

    /// <summary>
    /// The render/working density (device pixels per logical unit) of the canvas this brush fills into
    /// (feature 003, A-1/T042). <c>1f</c> = logical == device (byte-identical to pre-feature). A tile / image /
    /// drawable brush rasterizes its content into an intermediate sized <c>ceil(IntermediateSize × Scale)</c>
    /// and its shader local-matrix is compensated by <c>Scale(1/Scale)</c>, so the fill stays crisp under SSAA
    /// instead of being upscaled from a logical-resolution intermediate. CTM-following primitives ignore it.
    /// </summary>
    public float Scale { get; } = scale;

    /// <summary>
    /// The working-scale ceiling (feature 003, FR-037) of the render request this fill belongs to,
    /// forwarded into the nested pull a <see cref="DrawableBrush"/> performs for its child drawable —
    /// without it the child subtree falls back to <c>+∞</c> and a high-density source there escapes
    /// the request's ceiling. Canvas-managed fills pass <see cref="ImmediateCanvas.MaxWorkingScale"/>.
    /// </summary>
    public float MaxWorkingScale { get; } = maxWorkingScale;

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
            return new BrushConstructor(Bounds, presenter.Target, BlendMode, Scale).CreateShader();
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
        // feature 003 (A-1/T042): `s` is the canvas render density. The tile/image/drawable content is
        // rasterized into an intermediate sized `ceil(IntermediateSize × s)` and the shader local-matrix is
        // compensated by `Scale(1/s)`, so a brush FILL stays crisp under SSAA (s_out > 1) instead of being
        // upscaled from a logical-resolution intermediate. `contentDensity` is the skImage's
        // pixels-per-logical-content-unit (1 for a native bitmap; `s` for a DrawableBrush child re-rendered at
        // `s`). `s == 1` keeps the exact pre-feature path (every `× s` / `1/s` is a no-op → byte-identical).
        float s = Scale;
        RenderTarget? renderTarget = null;
        SKImage? skImage;
        PixelSize pixelSize;     // LOGICAL content size (drives the TileBrushCalculator)
        float contentDensity;    // skImage device px per logical content unit

        if (tileBrush is ImageBrush.Resource imageBrush
            && imageBrush.Source?.Bitmap is { } bitmap)
        {
            skImage = SKImage.FromBitmap(bitmap.SKBitmap);
            pixelSize = new(bitmap.Width, bitmap.Height);
            contentDensity = 1f; // the bitmap's native pixels ARE the logical content (1:1); ×s intermediate keeps them
        }
        else if (tileBrush is DrawableBrush.Resource drawableBrush)
        {
            if (drawableBrush.Drawable is null) return null;

            // Re-render the child at the render density `s` so the intermediate has real detail to sample
            // (not an upscaled logical render). Bounds/positions stay logical; the buffers are × s.
            var drawable = drawableBrush.Drawable;
            using var node = new DrawableRenderNode(drawable);
            using var context = new GraphicsContext2D(node, new PixelSize((int)Bounds.Width, (int)Bounds.Height), s);
            drawable.GetOriginal().Render(context, drawable);
            // Forward the request's FR-037 ceiling — without it the child subtree falls back to +∞
            // and a high-density source inside the brush escapes the preview/export cap.
            var processor = new RenderNodeProcessor(node, true, s, MaxWorkingScale);
            var ops = processor.RasterizeToRenderTargets();
            var totalBounds = ops.Aggregate(Rect.Empty, (current, item) => current.Union(item.Bounds));

            int dw = Math.Max(1, (int)MathF.Ceiling((float)totalBounds.Width * s));
            int dh = Math.Max(1, (int)MathF.Ceiling((float)totalBounds.Height * s));
            renderTarget = RenderTarget.Create(dw, dh);
            if (renderTarget == null)
            {
                // Without a shader the paint falls back to the plain white fill — make the failure visible.
                s_logger.LogWarning(
                    "DrawableBrush content buffer allocation failed ({Width}x{Height} px, density {Scale}); the fill degrades to solid white.",
                    dw, dh, s);
                return null;
            }

            using (var icanvas = new ImmediateCanvas(renderTarget, s, MaxWorkingScale))
            {
                icanvas.Clear();

                foreach (var op in ops)
                {
                    // op.RenderTarget is already × s dense; place it at the × s device offset.
                    Point offset = (op.Bounds.Position - totalBounds.Position) * s;
                    icanvas.DrawRenderTarget(op.RenderTarget, offset);
                    op.RenderTarget.Dispose();
                }
            }

            pixelSize = new PixelSize((int)totalBounds.Width, (int)totalBounds.Height); // LOGICAL
            contentDensity = s;
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

            // Allocate the intermediate at the render density and draw the content into it densely. The draw
            // matrix maps skImage px -> logical content (÷ contentDensity) -> logical intermediate
            // (IntermediateTransform) -> dense intermediate (× s).
            int iw = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Width * s));
            int ih = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Height * s));

            intermediate = RenderTarget.Create(iw, ih);
            if (intermediate == null)
            {
                s_logger.LogWarning(
                    "Tile-brush intermediate allocation failed ({Width}x{Height} px, density {Scale}); the fill degrades to solid white.",
                    iw, ih, s);
                return null;
            }

            using (var canvas = new ImmediateCanvas(intermediate, s, MaxWorkingScale))
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

            // The texture (intermediate) is now × s denser. Skia samples it as
            //   texel = localMatrix⁻¹ · CTM⁻¹ · device, with the fill CTM = CreateScale(s).
            // To map device pixels 1:1 onto the dense texels (crisp), the local-matrix must apply Scale(1/s)
            // to the texture coordinates FIRST (un-densify them to logical), then the logical tile/user
            // mapping. i.e. localMatrix = tileTransform ∘ Scale(1/s). At s == 1 this is a no-op.
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
            renderTarget?.Dispose();
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
