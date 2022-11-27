using System.Runtime.CompilerServices;

using Beutl.Graphics.Filters;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.TextFormatting;
using Beutl.Threading;

using SkiaSharp;

[assembly: InternalsVisibleTo("Beutl.ProjectSystem")]
[assembly: InternalsVisibleTo("Beutl")]

namespace Beutl.Graphics;

// https://github.com/AvaloniaUI/Avalonia/blob/master/src/Skia/Avalonia.Skia/DrawingContextImpl.cs
public class Canvas : ICanvas
{
    private readonly SKSurface _surface;
    internal readonly SKCanvas _canvas;
    private readonly Dispatcher? _dispatcher;
    private readonly Stack<IBrush> _brushesStack = new();
    private readonly Stack<MaskInfo> _maskStack = new();
    private readonly Stack<IImageFilter?> _filterStack = new();
    private readonly Stack<float> _strokeWidthStack = new();
    private readonly Stack<BlendMode> _blendModeStack = new();
    private readonly SKPaint _sharedPaint = new();
    private Matrix _currentTransform;

    public Canvas(int width, int height)
    {
        _dispatcher = Dispatcher.Current;
        Size = new PixelSize(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        _surface = SKSurface.Create(info);

        _canvas = _surface.Canvas;
        _currentTransform = _canvas.TotalMatrix.ToMatrix();
    }

    ~Canvas()
    {
        Dispose();
    }

    public bool IsDisposed { get; private set; }

    public IBrush Foreground { get; set; } = Brushes.White;

    public IImageFilter? Filter { get; set; }

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
            _sharedPaint.Dispose();
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
        _sharedPaint.Reset();
        ConfigurePaint(_sharedPaint, new Size(bmp.Width, bmp.Height));

        if (bmp is Bitmap<Bgra8888>)
        {
            using var img = SKImage.FromPixels(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888), bmp.Data);

            _canvas.DrawImage(img, SKPoint.Empty, _sharedPaint);
        }
        else
        {
            using var skbmp = bmp.ToSKBitmap();
            _canvas.DrawBitmap(skbmp, SKPoint.Empty, _sharedPaint);
        }
    }

    public void DrawCircle(Size size)
    {
        VerifyAccess();
        _sharedPaint.Reset();
        ConfigurePaint(_sharedPaint, size);
        float line = StrokeWidth;

        if (line >= MathF.Min(size.Width, size.Height) / 2)
            line = MathF.Min(size.Width, size.Height) / 2;

        float min = MathF.Min(size.Width, size.Height);

        if (line < min) min = line;
        if (min < 0) min = 0;


        _sharedPaint.Style = SKPaintStyle.Stroke;
        _sharedPaint.StrokeWidth = min;

        _canvas.DrawOval(
            size.Width / 2, size.Height / 2,
            (size.Width - min) / 2, (size.Height - min) / 2,
            _sharedPaint);
    }

    public void DrawRect(Size size)
    {
        VerifyAccess();
        _sharedPaint.Reset();
        ConfigurePaint(_sharedPaint, size);
        float stroke = Math.Min(StrokeWidth, Math.Min(size.Width, size.Height));

        _sharedPaint.Style = SKPaintStyle.Stroke;
        _sharedPaint.StrokeWidth = stroke;

        _canvas.DrawRect(
            stroke / 2, stroke / 2,
            size.Width - stroke, size.Height - stroke,
            _sharedPaint);
    }

    public void DrawText(FormattedText text)
    {
        VerifyAccess();
        _sharedPaint.Reset();

        var typeface = new Typeface(text.Font, text.Style, text.Weight);
        SKTypeface sktypeface = typeface.ToSkia();
        ConfigurePaint(_sharedPaint, text.Bounds);
        _sharedPaint.TextSize = text.Size;
        _sharedPaint.Typeface = sktypeface;
        _sharedPaint.Style = SKPaintStyle.Fill;
        Span<char> sc = stackalloc char[1];
        float prevRight = 0;

        foreach (char item in text.Text.AsSpan())
        {
            sc[0] = item;
            var bounds = default(SKRect);
            float w = _sharedPaint.MeasureText(sc, ref bounds);

            _canvas.Save();
            _canvas.Translate(prevRight + bounds.Left, 0);

            SKPath path = _sharedPaint.GetTextPath(
                sc,
                (bounds.Width / 2) - bounds.MidX,
                0/*-_paint.FontMetrics.Ascent*/);

            _canvas.DrawPath(path, _sharedPaint);
            path.Dispose();

            prevRight += text.Spacing;
            prevRight += w;

            _canvas.Restore();
        }
    }

    [Obsolete("Use 'DrawText(FormattedText)'.")]
    public void DrawText(Media.TextFormatting.Compat.TextElement text, Size size)
    {
        VerifyAccess();
        _sharedPaint.Reset();
        ConfigurePaint(_sharedPaint, size);
        _sharedPaint.TextSize = text.Size;
        _sharedPaint.Typeface = text.Typeface.ToSkia();
        _sharedPaint.Style = SKPaintStyle.Fill;
        Span<char> sc = stackalloc char[1];
        float prevRight = 0;

        foreach (char item in text.Text)
        {
            sc[0] = item;
            var bounds = default(SKRect);
            float w = _sharedPaint.MeasureText(sc, ref bounds);

            _canvas.Save();
            _canvas.Translate(prevRight + bounds.Left, 0);

            SKPath path = _sharedPaint.GetTextPath(
                sc,
                (bounds.Width / 2) - bounds.MidX,
                0/*-_paint.FontMetrics.Ascent*/);

            _canvas.DrawPath(path, _sharedPaint);
            path.Dispose();

            prevRight += text.Spacing;
            prevRight += w;

            _canvas.Restore();
        }
    }

    public void FillCircle(Size size)
    {
        VerifyAccess();
        _sharedPaint.Reset();
        ConfigurePaint(_sharedPaint, size);
        _sharedPaint.Style = SKPaintStyle.Fill;

        _canvas.DrawOval(SKPoint.Empty, size.ToSKSize(), _sharedPaint);
    }

    public void FillRect(Size size)
    {
        VerifyAccess();
        _sharedPaint.Reset();
        ConfigurePaint(_sharedPaint, size);

        _sharedPaint.Style = SKPaintStyle.Fill;

        _canvas.DrawRect(0, 0, size.Width, size.Height, _sharedPaint);
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

    public PushedState PushOpacityMask(IBrush mask, Rect bounds, bool invert = false)
    {
        VerifyAccess();
        var paint = new SKPaint();

        int level = _canvas.SaveLayer(paint);
        ConfigurePaint(paint, bounds.Size, mask, (BlendMode)paint.BlendMode, null, paint.StrokeWidth);
        _maskStack.Push(new MaskInfo(invert, paint));
        return new PushedState(this, level, PushedStateType.OpacityMask);
    }

    public void PopOpacityMask(int level = -1)
    {
        VerifyAccess();
        MaskInfo maskInfo = _maskStack.Pop();
        _sharedPaint.Reset();
        _sharedPaint.BlendMode = maskInfo.Invert ? SKBlendMode.DstOut : SKBlendMode.DstIn;

        _canvas.SaveLayer(_sharedPaint);
        using (SKPaint maskPaint = maskInfo.Paint)
        {
            _canvas.DrawPaint(maskPaint);
        }

        _canvas.Restore();

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

    public PushedState PushFilters(IImageFilter? filter)
    {
        VerifyAccess();
        int level = _filterStack.Count;
        _filterStack.Push(Filter);
        Filter = filter;
        return new PushedState(this, level, PushedStateType.Filter);
    }

    public void PopFilters(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _filterStack.Count - 1 : level;

        while (_filterStack.Count > level &&
            _filterStack.TryPop(out IImageFilter? state))
        {
            Filter = state;
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
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(Canvas));

        _dispatcher?.VerifyAccess();
    }

    private static void ConfigureGradientBrush(SKPaint paint, Size targetSize, IGradientBrush gradientBrush)
    {
        var tileMode = gradientBrush.SpreadMethod.ToSKShaderTileMode();
        SKColor[] stopColors = gradientBrush.GradientStops.SelectArray(s => s.Color.ToSKColor());
        float[] stopOffsets = gradientBrush.GradientStops.SelectArray(s => s.Offset);

        switch (gradientBrush)
        {
            case ILinearGradientBrush linearGradient:
                {
                    var start = linearGradient.StartPoint.ToPixels(targetSize).ToSKPoint();
                    var end = linearGradient.EndPoint.ToPixels(targetSize).ToSKPoint();

                    if (linearGradient.Transform is null)
                    {
                        using (var shader = SKShader.CreateLinearGradient(start, end, stopColors, stopOffsets, tileMode))
                        {
                            paint.Shader = shader;
                        }
                    }
                    else
                    {
                        Point transformOrigin = linearGradient.TransformOrigin.ToPixels(targetSize);
                        var offset = Matrix.CreateTranslation(transformOrigin);
                        Matrix transform = (-offset) * linearGradient.Transform.Value * offset;

                        using (var shader = SKShader.CreateLinearGradient(start, end, stopColors, stopOffsets, tileMode, transform.ToSKMatrix()))
                        {
                            paint.Shader = shader;
                        }
                    }

                    break;
                }
            case IRadialGradientBrush radialGradient:
                {
                    var center = radialGradient.Center.ToPixels(targetSize).ToSKPoint();
                    float radius = radialGradient.Radius * targetSize.Width;
                    var origin = radialGradient.GradientOrigin.ToPixels(targetSize).ToSKPoint();

                    if (origin.Equals(center))
                    {
                        // when the origin is the same as the center the Skia RadialGradient acts the same as D2D
                        if (radialGradient.Transform is null)
                        {
                            using (var shader = SKShader.CreateRadialGradient(center, radius, stopColors, stopOffsets, tileMode))
                            {
                                paint.Shader = shader;
                            }
                        }
                        else
                        {
                            Point transformOrigin = radialGradient.TransformOrigin.ToPixels(targetSize);
                            var offset = Matrix.CreateTranslation(transformOrigin);
                            Matrix transform = (-offset) * radialGradient.Transform.Value * (offset);

                            using (var shader = SKShader.CreateRadialGradient(center, radius, stopColors, stopOffsets, tileMode, transform.ToSKMatrix()))
                            {
                                paint.Shader = shader;
                            }
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
                            using (var shader = SKShader.CreateCompose(
                                SKShader.CreateColor(reversedColors[0]),
                                SKShader.CreateTwoPointConicalGradient(center, radius, origin, 0, reversedColors, reversedStops, tileMode)
                            ))
                            {
                                paint.Shader = shader;
                            }
                        }
                        else
                        {

                            Point transformOrigin = radialGradient.TransformOrigin.ToPixels(targetSize);
                            var offset = Matrix.CreateTranslation(transformOrigin);
                            Matrix transform = (-offset) * radialGradient.Transform.Value * (offset);

                            using (var shader = SKShader.CreateCompose(
                                SKShader.CreateColor(reversedColors[0]),
                                SKShader.CreateTwoPointConicalGradient(center, radius, origin, 0, reversedColors, reversedStops, tileMode, transform.ToSKMatrix())
                            ))
                            {
                                paint.Shader = shader;
                            }
                        }
                    }

                    break;
                }
            case IConicGradientBrush conicGradient:
                {
                    var center = conicGradient.Center.ToPixels(targetSize).ToSKPoint();

                    // Skia's default is that angle 0 is from the right hand side of the center point
                    // but we are matching CSS where the vertical point above the center is 0.
                    float angle = conicGradient.Angle - 90;
                    var rotation = SKMatrix.CreateRotationDegrees(angle, center.X, center.Y);

                    if (conicGradient.Transform is { })
                    {
                        Point transformOrigin = conicGradient.TransformOrigin.ToPixels(targetSize);
                        var offset = Matrix.CreateTranslation(transformOrigin);
                        Matrix transform = (-offset) * conicGradient.Transform.Value * (offset);

                        rotation = rotation.PreConcat(transform.ToSKMatrix());
                    }

                    using (var shader = SKShader.CreateSweepGradient(center, stopColors, stopOffsets, rotation))
                    {
                        paint.Shader = shader;
                    }

                    break;
                }
        }
    }

    private static void ConfigureTileBrush(SKPaint paint, Size targetSize, ITileBrush tileBrush)
    {
        IBitmap? bitmap;
        if (tileBrush is IDrawableBrush { Drawable: { } } drawableBrush)
        {
            bitmap = drawableBrush.Drawable.ToBitmap();
        }
        else if ((tileBrush as IImageBrush)?.Source?.Read(out bitmap) == true)
        {
        }
        else
        {
            throw new InvalidOperationException();
        }

        var calc = new TileBrushCalculator(tileBrush, new Size(bitmap.Width, bitmap.Height), targetSize);
        SKSizeI intermediateSize = calc.IntermediateSize.ToSKSize().ToSizeI();

        var intermediate = new SKBitmap(new SKImageInfo(intermediateSize.Width, intermediateSize.Height, SKColorType.Bgra8888));
        using (var canvas = new SKCanvas(intermediate))
        {
            using var target = bitmap.ToSKBitmap();
            using var ipaint = new SKPaint();
            ipaint.FilterQuality = tileBrush.BitmapInterpolationMode.ToSKFilterQuality();

            canvas.Clear();
            canvas.Save();
            canvas.ClipRect(calc.IntermediateClip.ToSKRect());
            canvas.SetMatrix(calc.IntermediateTransform.ToSKMatrix());

            canvas.DrawBitmap(target, (SKPoint)default, ipaint);
            canvas.Restore();
        }

        bitmap.Dispose();

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


        if (tileBrush.Transform is { })
        {
            Point origin = tileBrush.TransformOrigin.ToPixels(targetSize);
            var offset = Matrix.CreateTranslation(origin);
            Matrix transform = (-offset) * tileBrush.Transform.Value * offset;

            tileTransform = tileTransform.PreConcat(transform.ToSKMatrix());
        }

        SKShader shader = intermediate.ToShader(tileX, tileY, tileTransform);

        paint.Shader = shader;
    }

    private void ConfigurePaint(SKPaint paint, Size targetSize)
    {
        ConfigurePaint(paint, targetSize, Foreground, BlendMode, Filter, StrokeWidth);
    }

    private static void ConfigurePaint(SKPaint paint, Size targetSize, IBrush foreground, BlendMode blendMode, IImageFilter? filters, float strokeWidth)
    {
        double opacity = foreground.Opacity;
        paint.StrokeWidth = strokeWidth;
        paint.IsAntialias = true;
        paint.BlendMode = (SKBlendMode)blendMode;
        paint.ImageFilter?.Dispose();
        paint.ImageFilter = null;
        if (filters != null)
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

    private readonly record struct MaskInfo(bool Invert, SKPaint Paint);

    private readonly struct TileBrushCalculator
    {
        private readonly Size _imageSize;
        private readonly Rect _drawRect;

        public TileBrushCalculator(ITileBrush brush, Size contentSize, Size targetSize)
            : this(
                  brush.TileMode,
                  brush.Stretch,
                  brush.AlignmentX,
                  brush.AlignmentY,
                  brush.SourceRect,
                  brush.DestinationRect,
                  contentSize,
                  targetSize)
        {
        }

        public TileBrushCalculator(
            TileMode tileMode,
            Stretch stretch,
            AlignmentX alignmentX,
            AlignmentY alignmentY,
            RelativeRect sourceRect,
            RelativeRect destinationRect,
            Size contentSize,
            Size targetSize)
        {
            _imageSize = contentSize;

            SourceRect = sourceRect.ToPixels(_imageSize);
            DestinationRect = destinationRect.ToPixels(targetSize);

            Vector scale = stretch.CalculateScaling(DestinationRect.Size, SourceRect.Size);
            Vector translate = CalculateTranslate(alignmentX, alignmentY, SourceRect, DestinationRect, scale);

            IntermediateSize = tileMode == TileMode.None ? targetSize : DestinationRect.Size;
            IntermediateTransform = CalculateIntermediateTransform(
                tileMode,
                SourceRect,
                DestinationRect,
                scale,
                translate,
                out _drawRect);
        }

        public Rect DestinationRect { get; }

        public Rect IntermediateClip => _drawRect;

        public Size IntermediateSize { get; }

        public Matrix IntermediateTransform { get; }

        public bool NeedsIntermediate
        {
            get
            {
                if (IntermediateTransform != Matrix.Identity)
                    return true;
                if (SourceRect.Position != default)
                    return true;
                if (SourceRect.Size.AspectRatio == _imageSize.AspectRatio)
                    return false;
                if (SourceRect.Width != _imageSize.Width ||
                    SourceRect.Height != _imageSize.Height)
                    return true;
                return false;
            }
        }

        public Rect SourceRect { get; }

        public static Vector CalculateTranslate(
            AlignmentX alignmentX,
            AlignmentY alignmentY,
            Rect sourceRect,
            Rect destinationRect,
            Vector scale)
        {
            float x = 0.0f;
            float y = 0.0f;
            Size size = sourceRect.Size * scale;

            switch (alignmentX)
            {
                case AlignmentX.Center:
                    x += (destinationRect.Width - size.Width) / 2;
                    break;
                case AlignmentX.Right:
                    x += destinationRect.Width - size.Width;
                    break;
            }

            switch (alignmentY)
            {
                case AlignmentY.Center:
                    y += (destinationRect.Height - size.Height) / 2;
                    break;
                case AlignmentY.Bottom:
                    y += destinationRect.Height - size.Height;
                    break;
            }

            return new Vector(x, y);
        }

        public static Matrix CalculateIntermediateTransform(
            TileMode tileMode,
            Rect sourceRect,
            Rect destinationRect,
            Vector scale,
            Vector translate,
            out Rect drawRect)
        {
            Matrix transform = Matrix.CreateTranslation(-sourceRect.Position) *
                               Matrix.CreateScale(scale) *
                               Matrix.CreateTranslation(translate);
            Rect dr;

            if (tileMode == TileMode.None)
            {
                dr = destinationRect;
                transform *= Matrix.CreateTranslation(destinationRect.Position);
            }
            else
            {
                dr = new Rect(destinationRect.Size);
            }

            drawRect = dr;

            return transform;
        }
    }
}
