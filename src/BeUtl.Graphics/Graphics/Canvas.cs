using System.Runtime.CompilerServices;

using BeUtl.Graphics.Filters;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.Media.TextFormatting;
using BeUtl.Threading;

using SkiaSharp;

[assembly: InternalsVisibleTo("BeUtl.ProjectSystem")]
[assembly: InternalsVisibleTo("BeUtl")]

namespace BeUtl.Graphics;

public class Canvas : ICanvas
{
    private readonly SKSurface _surface;
    internal readonly SKCanvas _canvas;
    private readonly SKPaint _paint;
    private readonly Dispatcher? _dispatcher;
    private readonly Stack<IBrush> _brushesStack = new();
    private readonly Stack<SKPaint> _maskStack = new();
    private readonly Stack<ImageFilter[]> _filtersStack = new();
    private readonly Stack<float> _strokeWidthStack = new();
    private readonly Stack<BlendMode> _blendModeStack = new();
    private readonly List<ImageFilter> _filters = new();
    private Matrix _currentTransform;

    public Canvas(int width, int height)
    {
        _dispatcher = Dispatcher.Current;
        Size = new PixelSize(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        _surface = SKSurface.Create(info);

        _canvas = _surface.Canvas;
        _paint = new SKPaint();
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    ~Canvas()
    {
        Dispose();
    }

    public bool IsDisposed { get; private set; }

    public IBrush Foreground { get; set; } = Brushes.White;

    public IReadOnlyList<ImageFilter> Filters
    {
        get => _filters;
        set
        {
            _filters.Clear();
            _filters.AddRange(value);
        }
    }

    public float StrokeWidth { get; set; }

    public BlendMode BlendMode { get; set; } = BlendMode.SrcOver;

    public PixelSize Size { get; }

    public Matrix Transform
    {
        get { return _currentTransform; }
        set
        {
            if (_currentTransform == value)
                return;

            _currentTransform = value;
            _canvas.SetMatrix(_currentTransform.ToSKMatrix());
        }
    }

    public void Clear()
    {
        VerifyAccess();
        _canvas.Clear();
    }

    public void Clear(Color color)
    {
        VerifyAccess();
        _canvas.Clear(color.ToSKColor());
    }

    public void ClipRect(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        _canvas.ClipRect(clip.ToSKRect(), operation.ToSKClipOperation());
    }

    public void ClipPath(SKPath path, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        _canvas.ClipPath(path, operation.ToSKClipOperation());
    }

    public void Dispose()
    {
        void DisposeCore()
        {
            _surface.Dispose();
            _paint.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }

        if (!IsDisposed)
        {
            if (_dispatcher == null)
            {
                DisposeCore();
            }
            else
            {
                _dispatcher?.Invoke(DisposeCore);
            }
        }
    }

    public void DrawBitmap(IBitmap bmp)
    {
        if (bmp.ByteCount <= 0)
            return;

        VerifyAccess();
        ConfigurePaint(_paint, new Size(bmp.Width, bmp.Height));

        if (bmp is Bitmap<Bgra8888>)
        {
            using var img = SKImage.FromPixels(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888), bmp.Data);

            _canvas.DrawImage(img, SKPoint.Empty, _paint);
        }
        else
        {
            using var skbmp = bmp.ToSKBitmap();
            _canvas.DrawBitmap(skbmp, SKPoint.Empty, _paint);
        }
    }

    public void DrawCircle(Size size)
    {
        VerifyAccess();
        ConfigurePaint(_paint, size);
        float line = StrokeWidth;

        if (line >= MathF.Min(size.Width, size.Height) / 2)
            line = MathF.Min(size.Width, size.Height) / 2;

        float min = MathF.Min(size.Width, size.Height);

        if (line < min) min = line;
        if (min < 0) min = 0;

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = min;

        _canvas.DrawOval(
            size.Width / 2, size.Height / 2,
            (size.Width - min) / 2, (size.Height - min) / 2,
            _paint);
    }

    public void DrawRect(Size size)
    {
        VerifyAccess();
        ConfigurePaint(_paint, size);
        float stroke = Math.Min(StrokeWidth, Math.Min(size.Width, size.Height));

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = stroke;

        _canvas.DrawRect(
            stroke / 2, stroke / 2,
            size.Width - stroke, size.Height - stroke,
            _paint);
    }

    public void FillCircle(Size size)
    {
        VerifyAccess();
        ConfigurePaint(_paint, size);
        _paint.Style = SKPaintStyle.Fill;

        _canvas.DrawOval(SKPoint.Empty, size.ToSkia(), _paint);
    }

    public void FillRect(Size size)
    {
        VerifyAccess();
        ConfigurePaint(_paint, size);

        _paint.Style = SKPaintStyle.Fill;

        _canvas.DrawRect(0, 0, size.Width, size.Height, _paint);
    }

    // Marginを考慮しない
    public void DrawText(TextElement text)
    {
        VerifyAccess();
        ConfigurePaint(_paint, text.Measure());
        _paint.TextSize = text.Size;
        _paint.Typeface = text.Typeface.ToSkia();
        _paint.Style = SKPaintStyle.Fill;
        Span<char> sc = stackalloc char[1];
        float prevRight = 0;

        foreach (char item in text.Text)
        {
            sc[0] = item;
            var bounds = default(SKRect);
            float w = _paint.MeasureText(sc, ref bounds);

            _canvas.Save();
            _canvas.Translate(prevRight + bounds.Left, 0);

            SKPath path = _paint.GetTextPath(
                sc,
                (bounds.Width / 2) - bounds.MidX,
                0/*-_paint.FontMetrics.Ascent*/);

            _canvas.DrawPath(path, _paint);
            path.Dispose();

            prevRight += text.Spacing;
            prevRight += w;

            _canvas.Restore();
        }
    }

    public unsafe Bitmap<Bgra8888> GetBitmap()
    {
        VerifyAccess();
        var result = new Bitmap<Bgra8888>(Size.Width, Size.Height);

        _surface.ReadPixels(new SKImageInfo(Size.Width, Size.Height, SKColorType.Bgra8888), result.Data, result.Width * sizeof(Bgra8888), 0, 0);

        return result;
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        int level = _canvas.Save();
        ClipRect(clip, operation);
        return new PushedState(this, level, PushedStateType.Clip);
    }

    public void PopClip(int level = -1)
    {
        VerifyAccess();
        _canvas.RestoreToCount(level);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    public PushedState PushCanvas()
    {
        VerifyAccess();
        int level = _canvas.Save();
        return new PushedState(this, level, PushedStateType.Canvas);
    }

    public void PopCanvas(int level = -1)
    {
        VerifyAccess();
        _canvas.RestoreToCount(level);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    public PushedState PushOpacityMask(IBrush mask, Rect bounds)
    {
        VerifyAccess();
        var paint = new SKPaint();

        int level = _canvas.SaveLayer(paint);
        ConfigurePaint(paint, bounds.Size, mask, (BlendMode)paint.BlendMode, null, paint.StrokeWidth);
        _maskStack.Push(paint);
        return new PushedState(this, level, PushedStateType.OpacityMask);
    }

    public void PopOpacityMask(int level = -1)
    {
        VerifyAccess();
        using (var paint = new SKPaint { BlendMode = SKBlendMode.DstIn })
        {
            _canvas.SaveLayer(paint);
            using (SKPaint maskPaint = _maskStack.Pop())
            {
                _canvas.DrawPaint(maskPaint);
            }
            _canvas.Restore();
        }

        _canvas.RestoreToCount(level);
    }

    public PushedState PushForeground(IBrush brush)
    {
        VerifyAccess();
        int level = _brushesStack.Count;
        _brushesStack.Push(Foreground);
        Foreground = brush;
        return new PushedState(this, level, PushedStateType.Foreground);
    }

    public void PopForeground(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _brushesStack.Count - 1 : level;

        while (_brushesStack.Count > level &&
            _brushesStack.TryPop(out IBrush? state))
        {
            Foreground = state;
        }
    }

    public PushedState PushStrokeWidth(float strokeWidth)
    {
        VerifyAccess();
        int level = _strokeWidthStack.Count;
        _strokeWidthStack.Push(StrokeWidth);
        StrokeWidth = strokeWidth;
        return new PushedState(this, level, PushedStateType.StrokeWidth);
    }

    public void PopStrokeWidth(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _strokeWidthStack.Count - 1 : level;

        while (_strokeWidthStack.Count > level &&
            _strokeWidthStack.TryPop(out float state))
        {
            StrokeWidth = state;
        }
    }

    public PushedState PushFilters(ImageFilters filters)
    {
        VerifyAccess();
        int level = _filtersStack.Count;
        _filtersStack.Push(Filters.ToArray());
        Filters = filters;
        return new PushedState(this, level, PushedStateType.Filters);
    }

    public void PopFilters(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _filtersStack.Count - 1 : level;

        while (_filtersStack.Count > level &&
            _filtersStack.TryPop(out ImageFilter[]? state))
        {
            _filters.Clear();
            _filters.AddRange(state);
        }
    }

    public PushedState PushBlendMode(BlendMode blendMode)
    {
        VerifyAccess();
        int level = _blendModeStack.Count;
        _blendModeStack.Push(BlendMode);
        BlendMode = blendMode;
        return new PushedState(this, level, PushedStateType.BlendMode);
    }

    public void PopBlendMode(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _blendModeStack.Count - 1 : level;

        while (_blendModeStack.Count > level &&
            _blendModeStack.TryPop(out BlendMode state))
        {
            BlendMode = state;
        }
    }

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
    {
        VerifyAccess();
        int level = _canvas.Save();

        if (transformOperator == TransformOperator.Prepend)
        {
            Transform = Transform.Prepend(matrix);
        }
        else if (transformOperator == TransformOperator.Append)
        {
            Transform = Transform.Append(matrix);
        }
        else
        {
            Transform = matrix;
        }

        return new PushedState(this, level, PushedStateType.Transform);
    }

    public void PopTransform(int level = -1)
    {
        VerifyAccess();
        _canvas.RestoreToCount(level);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    public void RotateDegrees(float degrees)
    {
        VerifyAccess();
        _canvas.RotateDegrees(degrees);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    public void RotateRadians(float radians)
    {
        VerifyAccess();
        _canvas.RotateRadians(radians);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    public void Scale(Vector vector)
    {
        VerifyAccess();
        _canvas.Scale(vector.X, vector.Y);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    public void Skew(Vector vector)
    {
        VerifyAccess();
        _canvas.Skew(vector.X, vector.Y);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    public void Translate(Vector vector)
    {
        VerifyAccess();
        _canvas.Translate(vector.X, vector.Y);
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    private void VerifyAccess()
    {
        _dispatcher?.VerifyAccess();
    }

    private static void ConfigureGradientBrush(SKPaint paint, Size targetSize, IGradientBrush gradientBrush)
    {
        var tileMode = gradientBrush.SpreadMethod.ToSKShaderTileMode();
        SKColor[] stopColors = gradientBrush.GradientStops.Select(s => s.Color.ToSKColor()).ToArray();
        float[] stopOffsets = gradientBrush.GradientStops.Select(s => (float)s.Offset).ToArray();

        switch (gradientBrush)
        {
            case ILinearGradientBrush linearGradient:
                {
                    var start = linearGradient.StartPoint.ToPixels(targetSize).ToSKPoint();
                    var end = linearGradient.EndPoint.ToPixels(targetSize).ToSKPoint();

                    using (var shader = SKShader.CreateLinearGradient(start, end, stopColors, stopOffsets, tileMode))
                    {
                        paint.Shader = shader;
                    }

                    break;
                }
            case IRadialGradientBrush radialGradient:
                {
                    SKPoint center = radialGradient.Center.ToPixels(targetSize).ToSKPoint();
                    float radius = radialGradient.Radius * targetSize.Width;

                    var origin = radialGradient.GradientOrigin.ToPixels(targetSize).ToSKPoint();

                    if (origin.Equals(center))
                    {
                        // when the origin is the same as the center the Skia RadialGradient acts the same as D2D
                        using (var shader = SKShader.CreateRadialGradient(center, radius, stopColors, stopOffsets, tileMode))
                        {
                            paint.Shader = shader;
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
                        using (var shader = SKShader.CreateCompose(
                            SKShader.CreateColor(reversedColors[0]),
                            SKShader.CreateTwoPointConicalGradient(center, radius, origin, 0, reversedColors, reversedStops, tileMode)
                        ))
                        {
                            paint.Shader = shader;
                        }
                    }

                    break;
                }
            case IConicGradientBrush conicGradient:
                {
                    var center = conicGradient.Center.ToPixels(targetSize).ToSKPoint();

                    // Skia's default is that angle 0 is from the right hand side of the center point
                    // but we are matching CSS where the vertical point above the center is 0.
                    float angle = (float)(conicGradient.Angle - 90);
                    var rotation = SKMatrix.CreateRotationDegrees(angle, center.X, center.Y);

                    using (var shader =
                        SKShader.CreateSweepGradient(center, stopColors, stopOffsets, rotation))
                    {
                        paint.Shader = shader;
                    }

                    break;
                }
        }
    }

    private static void ConfigureTileBrush(SKPaint paint, Size targetSize, ITileBrush tileBrush)
    {
        Rect tileDstRect = tileBrush.DestinationRect.ToPixels(targetSize);
        SKMatrix tileTransform = tileBrush.TileMode != TileMode.None
            ? SKMatrix.CreateTranslation(-tileDstRect.X, -tileDstRect.Y)
            : SKMatrix.CreateIdentity();

        SKShaderTileMode tileX = tileBrush.TileMode == TileMode.None
            ? SKShaderTileMode.Clamp
            : tileBrush.TileMode == TileMode.FlipX || tileBrush.TileMode == TileMode.FlipXY
                ? SKShaderTileMode.Mirror
                : SKShaderTileMode.Repeat;

        SKShaderTileMode tileY = tileBrush.TileMode == TileMode.None
            ? SKShaderTileMode.Clamp
            : tileBrush.TileMode == TileMode.FlipY || tileBrush.TileMode == TileMode.FlipXY
                ? SKShaderTileMode.Mirror
                : SKShaderTileMode.Repeat;

        var paintTransform = default(SKMatrix);

        paintTransform = SKMatrix.Concat(paintTransform, tileTransform);

        if (tileBrush is IDrawableBrush { Drawable: { IsDisposed: false } } drawableBrush)
        {
            using IBitmap bmp = drawableBrush.Drawable.ToBitmap();
            using var skbmp = bmp.ToSKBitmap();
            using var shader = SKShader.CreateBitmap(skbmp, tileX, tileY, paintTransform);

            paint.Shader = shader;
        }
        else if (tileBrush is IImageBrush { Source: { IsDisposed: false } } imageBrush)
        {
            using var bmp = imageBrush.Source.ToSKBitmap();
            using var shader = SKShader.CreateBitmap(bmp, tileX, tileY, paintTransform);

            paint.Shader = shader;
        }
    }

    private void ConfigurePaint(SKPaint paint, Size targetSize)
    {
        ConfigurePaint(paint, targetSize, Foreground, BlendMode, Filters, StrokeWidth);
    }

    private static void ConfigurePaint(SKPaint paint, Size targetSize, IBrush foreground, BlendMode blendMode, IReadOnlyList<ImageFilter>? filters, float strokeWidth)
    {
        double opacity = foreground.Opacity;
        paint.StrokeWidth = strokeWidth;
        paint.IsAntialias = true;
        paint.BlendMode = (SKBlendMode)blendMode;
        paint.ImageFilter?.Dispose();
        paint.ImageFilter = null;
        if (filters != null && filters.Count > 0)
        {
            paint.ImageFilter = filters.ToSKImageFilter();
        }

        paint.Color = new SKColor(255, 255, 255, (byte)(255 * opacity));

        if (foreground is ISolidColorBrush solid)
        {
            paint.Color = new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * opacity));
        }
        else if (foreground is IGradientBrush gradient)
        {
            ConfigureGradientBrush(paint, targetSize, gradient);
        }
        else if (foreground is ITileBrush tileBrush)
        {
            ConfigureTileBrush(paint, targetSize, tileBrush);
        }
        else
        {
            paint.Color = new SKColor(255, 255, 255, 0);
        }
    }
}
