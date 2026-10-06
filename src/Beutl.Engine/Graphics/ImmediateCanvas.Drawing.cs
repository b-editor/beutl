using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    public void DrawDrawable(Drawable.Resource drawable)
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        using var node = new DrawableRenderNode(drawable);
        using var context = new GraphicsContext2D(node, LogicalSize, _currentDensity);
        drawable.RequireOriginal().Render(context, drawable);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest
            {
                Intent = Intent,
                OutputScale = _currentDensity,
                MaxWorkingScale = MaxWorkingScale,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
            });
        renderer.Render(this);
    }

    public void DrawNode(RenderNode node)
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest
            {
                Intent = Intent,
                OutputScale = _currentDensity,
                MaxWorkingScale = MaxWorkingScale,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
            });
        renderer.Render(this);
    }

    public void DrawBackdrop(IBackdrop backdrop)
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        backdrop.Draw(this);
    }

    public IBackdrop Snapshot()
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        // Use SurfaceDensity (not Density, which PushDeviceSpace lowers to 1) so the backdrop un-scales correctly.
        return new TmpBackdrop(_renderTarget.Snapshot(), SurfaceDensity);
    }

    public void DrawBitmap(Bitmap bmp, Brush.Resource? fill, Pen.Resource? pen)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);

        if (bmp.ByteCount <= 0)
            return;

        VerifyPixelOperation();
        VerifyCallbackResource(bmp, nameof(bmp));
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        var size = new Size(bmp.Width, bmp.Height);
        ConfigureFillPaint(new(size), fill);

        using var img = SKImage.FromBitmap(bmp.SKBitmap);

        SKSamplingOptions sampling = RenderScaleUtilities.IsExactIntegerReduction(SurfaceDensity)
            ? s_compositeSampling
            : s_bitmapSampling;
        Canvas.DrawImage(img, 0, 0, sampling, _sharedFillPaint);
    }

    // Draw a bitmap into a logical destination rect (Mitchell resample).
    public void DrawBitmapScaled(Bitmap bmp, Rect dest, Brush.Resource? fill)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);

        if (bmp.ByteCount <= 0)
            return;

        VerifyPixelOperation();
        VerifyCallbackResource(bmp, nameof(bmp));
        VerifyCallbackResource(fill, nameof(fill));
        ConfigureFillPaint(new(dest.Size), fill);

        using var img = SKImage.FromBitmap(bmp.SKBitmap);
        var src = SKRect.Create(bmp.Width, bmp.Height);

        Canvas.DrawImage(img, src, dest.ToSKRect(), new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    public void DrawImageSource(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        if (_executionToken is null)
            VerifyNestedExecutionOperation();
        else
            VerifyCallbackResource(source, nameof(source));
        var bitmap = source.Bitmap;
        if (bitmap != null)
        {
            if (_executionToken is null)
                DrawBitmap(bitmap, fill, pen);
            else
                _executionToken.AuthorizeResource(bitmap, () => DrawBitmap(bitmap, fill, pen));
        }
    }

    public void DrawVideoSource(VideoSource.Resource source, TimeSpan frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        if (_executionToken is null)
            VerifyNestedExecutionOperation();
        else
            VerifyCallbackResource(source, nameof(source));
        Rational rate = source.FrameRate;
        double frameNum = frame.TotalSeconds * (rate.Numerator / (double)rate.Denominator);
        DrawVideoSource(source, (int)frameNum, fill, pen);
    }

    public void DrawVideoSource(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        if (_executionToken is null)
            VerifyNestedExecutionOperation();
        else
            VerifyCallbackResource(source, nameof(source));
        if (source.Read(frame, out var bitmapRef))
        {
            using (bitmapRef)
            {
                void DrawFrame()
                {
                    if (source.ProxyResolution == null)
                    {
                        DrawBitmap(bitmapRef.Value, fill, pen);
                    }
                    else
                    {
                        var dest = new Rect(default, source.LogicalFrameSize.ToSize(1));
                        DrawBitmapScaled(bitmapRef.Value, dest, fill);
                    }
                }

                if (_executionToken is null)
                    DrawFrame();
                else
                    _executionToken.AuthorizeResource(bitmapRef.Value, DrawFrame);
            }
        }
    }

    public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        ConfigureFillPaint(rect, fill);
        Canvas.DrawOval(rect.ToSKRect(), _sharedFillPaint);

        if (pen != null && pen.Thickness != 0)
        {
            using (var builder = new SKPathBuilder())
            {
                builder.AddOval(rect.ToSKRect());
                using SKPath path = builder.Detach();
                DrawSKPath(path, true, fill, pen);
            }
        }
    }

    public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        ConfigureFillPaint(rect, fill);
        Canvas.DrawRect(rect.ToSKRect(), _sharedFillPaint);

        if (pen != null && pen.Thickness != 0)
        {
            using (var builder = new SKPathBuilder())
            {
                builder.AddRect(rect.ToSKRect());
                using SKPath path = builder.Detach();
                DrawSKPath(path, true, fill, pen);
            }
        }
    }

    public void DrawText(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(text, nameof(text));
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        float density = _currentDensity;
        SKTextBlob? textBlob = text.GetTextBlob(density);
        if (textBlob is null)
        {
            // Empty text shapes to no glyphs, so there is nothing to fill or stroke.
            return;
        }

        if (density == 1f)
        {
            ConfigureFillPaint(text.Bounds, fill);
            Canvas.DrawText(textBlob, 0, 0, _sharedFillPaint);

            if (pen != null
                && pen.Thickness > 0
                && text.GetStrokePath() is { } stroke)
            {
                ConfigureStrokePaint(new(text.Bounds.Size), pen);
                Canvas.DrawPath(stroke, _sharedStrokePaint);
            }
        }
        else
        {
            int count = Canvas.Save();
            try
            {
                Canvas.SetMatrix((SKMatrix44)CreateDensityScaledContentTransform(density).ToSKMatrix());

                // The blob is shaped at device density, so its glyphs already span Bounds * density
                // under this CTM. Pass scale 1 so the density isn't applied twice to brush patterns.
                ConfigureFillPaint(text.Bounds * density, fill, scale: 1f);
                Canvas.DrawText(textBlob, 0, 0, _sharedFillPaint);

                if (pen != null
                    && pen.Thickness > 0
                    && text.GetStrokePath(density) is { } stroke)
                {
                    ConfigureStrokePaint(new(text.Bounds.Size * density), pen, scale: 1f);
                    Canvas.DrawPath(stroke, _sharedStrokePaint);
                }
            }
            finally
            {
                Canvas.RestoreToCount(count);
            }
        }
    }

    private Matrix CreateDensityScaledContentTransform(float density)
    {
        if (density == 1f || _currentBaseTransform.IsIdentity)
        {
            return _currentTransform;
        }

        if (!_currentBaseTransform.TryInvert(out Matrix inverseBase))
        {
            return _currentTransform;
        }

        Matrix logicalTransform = _currentTransform.Append(inverseBase);
        return Matrix.CreateScale(1f / density, 1f / density)
            .Append(logicalTransform)
            .Append(_currentBaseTransform);
    }

    internal void DrawSKPath(SKPath skPath, bool strokeOnly, Brush.Resource? fill, Pen.Resource? pen)
    {
        Rect rect = skPath.Bounds.ToGraphicsRect();

        if (!strokeOnly)
        {
            ConfigureFillPaint(rect, fill);
            Canvas.DrawPath(skPath, _sharedFillPaint);
        }

        if (pen != null && pen.Thickness > 0)
        {
            ConfigureStrokePaint(rect, pen);

            using SKPath strokePath = PenHelper.CreateStrokePath(skPath, pen, rect);
            Canvas.DrawPath(strokePath, _sharedStrokePaint);
        }
    }

    public void DrawGeometry(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(geometry, nameof(geometry));
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        SKPath skPath = geometry.GetCachedPath();
        Rect rect = geometry.Bounds;

        ConfigureFillPaint(geometry.Bounds, fill);
        if (!TryDrawProductCoverageRectangle(geometry, _sharedFillPaint))
            Canvas.DrawPath(skPath, _sharedFillPaint);

        if (pen != null && pen.Thickness > 0)
        {
            ConfigureStrokePaint(rect, pen);
            SKPath? stroke = geometry.GetCachedStrokePath(pen);
            if (stroke != null)
            {
                Canvas.DrawPath(stroke, _sharedStrokePaint);
            }
        }
    }

    private bool TryDrawProductCoverageRectangle(Geometry.Resource geometry, SKPaint paint)
    {
        // Skia's path antialiasing may publish one-axis coverage at a fractional rectangle corner.
        // A Porter-Duff mask needs the geometric area product, because the same coverage is later
        // applied to every pixel in the isolated target-layer domain.
        if ((_directBlendMode != BlendMode.DstOut && !_productRectangleCoverage)
            || geometry.GetOriginal() is not RectGeometry
            || _currentTransform.M12 != 0
            || _currentTransform.M13 != 0
            || _currentTransform.M21 != 0
            || _currentTransform.M23 != 0
            || _currentTransform.M33 != 1)
        {
            return false;
        }

        var rectangle = (RectGeometry.Resource)geometry;
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return true;
        }

        SKColor previousColor = paint.Color;
        SKShader? previousShader = paint.Shader;
        using SKShader? ownedSourceShader = previousShader is null
            ? SKShader.CreateColor(new SKColor(
                previousColor.Red,
                previousColor.Green,
                previousColor.Blue,
                255))
            : null;
        SKShader sourceShader = previousShader ?? ownedSourceShader!;
        using var uniforms = new SKRuntimeEffectUniforms(s_rectCoverageEffect.Value);
        using var children = new SKRuntimeEffectChildren(s_rectCoverageEffect.Value);
        uniforms["left"] = (float)geometry.Bounds.Left;
        uniforms["top"] = (float)geometry.Bounds.Top;
        uniforms["right"] = (float)geometry.Bounds.Right;
        uniforms["bottom"] = (float)geometry.Bounds.Bottom;
        uniforms["scaleX"] = MathF.Abs(_currentTransform.M11);
        uniforms["scaleY"] = MathF.Abs(_currentTransform.M22);
        children["src"] = sourceShader;
        using SKShader coverageShader = s_rectCoverageEffect.Value.ToShader(uniforms, children);
        bool previousAntialias = paint.IsAntialias;
        try
        {
            paint.Color = new SKColor(255, 255, 255, previousColor.Alpha);
            paint.Shader = coverageShader;
            paint.IsAntialias = false;
            Canvas.DrawPaint(paint);
        }
        finally
        {
            paint.Shader = previousShader;
            paint.Color = previousColor;
            paint.IsAntialias = previousAntialias;
        }

        return true;
    }

    private static SKRuntimeEffect CreateRectCoverageEffect()
    {
        const string source =
            """
            uniform shader src;
            uniform float left;
            uniform float top;
            uniform float right;
            uniform float bottom;
            uniform float scaleX;
            uniform float scaleY;

            half4 main(float2 p)
            {
                float x = clamp((p.x - left) * scaleX + 0.5, 0.0, 1.0)
                    * clamp((right - p.x) * scaleX + 0.5, 0.0, 1.0);
                float y = clamp((p.y - top) * scaleY + 0.5, 0.0, 1.0)
                    * clamp((bottom - p.y) * scaleY + 0.5, 0.0, 1.0);
                return src.eval(p) * half(x * y);
            }
            """;
        return SKRuntimeEffect.CreateShader(source, out string? errorText)
               ?? throw new InvalidOperationException(
                   $"Failed to compile the rectangle coverage shader: {errorText}");
    }
}
