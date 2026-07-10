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
    float maxWorkingScale = float.PositiveInfinity, PipelineDiagnostics? diagnostics = null)
{
    private static readonly ILogger s_logger = Log.CreateLogger("BrushConstructor");

    public Rect Bounds { get; } = bounds;

    public Brush.Resource? Brush { get; } = brush;

    public BlendMode BlendMode { get; } = blendMode;

    /// <summary>
    /// Render density (device px per logical unit) of the canvas this brush fills into.
    /// Tile/image brushes rasterize intermediates at <c>ceil(size * Scale)</c> and compensate the shader matrix.
    /// </summary>
    public float Scale { get; } = scale;

    /// <summary>Working-scale ceiling forwarded into nested pulls (e.g. <see cref="DrawableBrush"/>).</summary>
    public float MaxWorkingScale { get; } = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);

    /// <summary>
    /// The owning renderer's effect-pipeline counters when this brush is constructed <em>during effect
    /// execution</em> (the migrated effects pass their pass's diagnostics), else <see langword="null"/> (standalone
    /// 2D drawing / tooling). A <see cref="DrawableBrush"/> renders a nested drawable to produce its shader; threading
    /// this makes that nested render observable on <c>IRenderer.Diagnostics</c> (FR-017): any effect inside the
    /// drawable counts on it, and the brush's own composition buffers count <see cref="PipelineDiagnostics.TargetAllocations"/>.
    /// The nested render is deliberately NOT pooled: a brush renders while its outer effect pass already holds its
    /// declared pooled leases, and an extra pool lease here would exceed the plan's static peak-live bound
    /// (execution-plan §C3.1, FR-007) — correctness over reuse.
    /// </summary>
    public PipelineDiagnostics? Diagnostics { get; } = diagnostics;

    /// <summary>
    /// Whether <see cref="CreateShader"/> would return a non-null shader for <paramref name="brush"/>, decided from
    /// the brush's SHAPE alone — no rendering, no target allocation, no GPU work (contract A1 / FR-001). This mirrors
    /// every null-return branch of <see cref="CreateShader"/> so a describe-time author can decide "does this brush
    /// yield a shader" without the describe-time render the removed <see cref="DisplacementMapTransform"/> probe did:
    /// a null/unknown brush, a <see cref="BrushPresenter"/> with no target, a gradient with no stops, an image brush
    /// with no bitmap, or a drawable brush with no drawable yields nothing. A brush WITH content (a drawable/image
    /// brush) is reported as producing a shader here; whether its allocation later succeeds is a render-time concern
    /// (an image brush without a bitmap instead throws in <see cref="CreateShader"/>, so this reports it as no-shader).
    /// </summary>
    public static bool CanCreateShader(Brush.Resource? brush) => brush switch
    {
        BrushPresenter.Resource presenter => presenter.Target is { } target && CanCreateShader(target),
        SolidColorBrush.Resource => true,
        GradientBrush.Resource gradient => gradient.GradientStops.Count > 0,
        ImageBrush.Resource image => image.Source?.Bitmap is not null,
        DrawableBrush.Resource drawable => drawable.Drawable is not null,
        PerlinNoiseBrush.Resource => true,
        _ => false,
    };

    public void ConfigurePaint(SKPaint paint)
    {
        // Handle BrushPresenter by delegating to the target brush
        if (Brush is BrushPresenter.Resource presenter && presenter.Target != null)
        {
            new BrushConstructor(Bounds, presenter.Target, BlendMode, Scale, MaxWorkingScale, Diagnostics)
                .ConfigurePaint(paint);
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
            return new BrushConstructor(Bounds, presenter.Target, BlendMode, Scale, MaxWorkingScale, Diagnostics)
                .CreateShader();
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
        float s = Scale;
        RenderTarget? renderTarget = null;
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
        else if (tileBrush is DrawableBrush.Resource drawableBrush)
        {
            if (drawableBrush.Drawable is null) return null;

            var drawable = drawableBrush.Drawable;
            using var node = new DrawableRenderNode(drawable);
            using var context = new GraphicsContext2D(node, new Size((int)Bounds.Width, (int)Bounds.Height), s);
            drawable.GetOriginal().Render(context, drawable);
            // Thread the owning renderer's diagnostics (not the pool) into the nested pull: an effect inside the
            // drawable then counts on IRenderer.Diagnostics (FR-017) instead of a throwaway instance. The pool is
            // deliberately withheld — see the Diagnostics doc (FR-007 static peak-live bound).
            var processor = new RenderNodeProcessor(node, true, s, MaxWorkingScale, Diagnostics);
            var ops = processor.RasterizeToRenderTargets();
            var totalBounds = ops.Aggregate(Rect.Empty, (current, item) => current.Union(item.Bounds));

            int dw = Math.Max(1, (int)MathF.Ceiling((float)totalBounds.Width * s));
            int dh = Math.Max(1, (int)MathF.Ceiling((float)totalBounds.Height * s));
            // Non-pooled but counted: Acquire(null, …) allocates directly yet records TargetAllocations.
            renderTarget = RenderTargetPool.Acquire(null, dw, dh, Diagnostics);
            if (renderTarget == null)
            {
                // Dispose ops that the blit loop below would have consumed.
                foreach (var op in ops)
                    op.RenderTarget.Dispose();

                s_logger.LogWarning(
                    "DrawableBrush content buffer allocation failed ({Width}x{Height} px, density {Scale}); preview fill degrades to solid white, delivery render fails fast.",
                    dw, dh, s);
                ThrowIfDeliveryAllocationFailure(
                    $"DrawableBrush content buffer allocation failed ({dw}x{dh} px, density {s}).");
                return null;
            }

            // Density 1: raw device-px blits with hand-computed offsets (no base CTM re-scale).
            using (var icanvas = new ImmediateCanvas(renderTarget, 1f, MaxWorkingScale))
            {
                icanvas.Clear();

                foreach (var op in ops)
                {
                    Point offset = (op.Bounds.Position - totalBounds.Position) * s;
                    icanvas.DrawRenderTarget(op.RenderTarget, offset);
                    op.RenderTarget.Dispose();
                }
            }

            pixelSize = new PixelSize((int)totalBounds.Width, (int)totalBounds.Height);
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

            int iw = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Width * s));
            int ih = Math.Max(1, (int)MathF.Ceiling((float)calc.IntermediateSize.Height * s));

            intermediate = RenderTargetPool.Acquire(null, iw, ih, Diagnostics);
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
            renderTarget?.Dispose();
        }
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
