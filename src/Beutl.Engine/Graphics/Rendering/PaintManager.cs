using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Manages paint configuration and reuse for rendering operations.
/// Extracted from ImmediateCanvas to improve separation of concerns.
/// </summary>
internal sealed class PaintManager : IDisposable
{
    private readonly SKPaint _fillPaint = new();
    private readonly SKPaint _strokePaint = new();
    private bool _disposed;

    public PaintManager()
    {
        MemoryManagement.TrackDisposable(this, DisposableCategory.Graphics);
        MemoryManagement.TrackSkiaObject(_fillPaint);
        MemoryManagement.TrackSkiaObject(_strokePaint);
    }

    public SKPaint FillPaint => _fillPaint;
    public SKPaint StrokePaint => _strokePaint;

    /// <summary>
    /// Configures the fill paint for the specified brush and bounds.
    /// </summary>
    public void ConfigureFillPaint(Rect bounds, IBrush? brush, BlendMode blendMode = BlendMode.SrcOver)
    {
        ThrowIfDisposed();
        
        _fillPaint.Reset();
        if (brush is not null)
        {
            new BrushConstructor(bounds, brush, blendMode).ConfigurePaint(_fillPaint);
        }
    }

    /// <summary>
    /// Configures the stroke paint for the specified pen and bounds.
    /// </summary>
    public void ConfigureStrokePaint(Rect bounds, IPen? pen, BlendMode blendMode = BlendMode.SrcOver)
    {
        ThrowIfDisposed();
        
        _strokePaint.Reset();

        if (pen is null || pen.Thickness == 0) return;

        Rect original = bounds;
        float thickness = pen.Thickness;

        switch (pen.StrokeAlignment)
        {
            case StrokeAlignment.Center:
                bounds = bounds.Inflate(thickness / 2);
                break;

            case StrokeAlignment.Outside:
                bounds = bounds.Inflate(thickness);
                thickness *= 2;
                break;

            case StrokeAlignment.Inside:
                thickness *= 2;
                float maxAspect = Math.Max(bounds.Width, bounds.Height);
                thickness = Math.Min(thickness, maxAspect);
                break;
        }

        _strokePaint.IsStroke = true;
        _strokePaint.StrokeWidth = thickness;
        _strokePaint.StrokeCap = (SKStrokeCap)pen.StrokeCap;
        _strokePaint.StrokeJoin = (SKStrokeJoin)pen.StrokeJoin;
        _strokePaint.StrokeMiter = pen.MiterLimit;

        if (pen.DashArray is { Count: > 0 } dashArray)
        {
            ConfigureDashEffect(dashArray, pen.DashOffset, thickness);
        }

        new BrushConstructor(original, pen.Brush, blendMode).ConfigurePaint(_strokePaint);
    }

    private void ConfigureDashEffect(IReadOnlyList<float> srcDashes, double dashOffset, float thickness)
    {
        int count = srcDashes.Count % 2 == 0 ? srcDashes.Count : srcDashes.Count * 2;
        float[] dashesArray = new float[count];

        for (int i = 0; i < count; ++i)
        {
            dashesArray[i] = (float)srcDashes[i % srcDashes.Count] * thickness;
        }

        float offset = (float)(dashOffset * thickness);
        var pathEffect = SKPathEffect.CreateDash(dashesArray, offset);
        MemoryManagement.TrackSkiaObject(pathEffect);
        _strokePaint.PathEffect = pathEffect;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            MemoryManagement.MarkDisposed(_fillPaint);
            MemoryManagement.MarkDisposed(_strokePaint);
            _fillPaint.Dispose();
            _strokePaint.Dispose();
            MemoryManagement.MarkDisposed(this);
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}