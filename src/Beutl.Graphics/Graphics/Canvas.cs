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
    private readonly Stack<ImageFilterInfo> _filterStack = new();
    private readonly Stack<IPen?> _penStack = new();
    private readonly Stack<float> _strokeWidthStack = new();
    private readonly Stack<BlendMode> _blendModeStack = new();
    private readonly SKPaint _sharedFillPaint = new();
    private readonly SKPaint _sharedStrokePaint = new();
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

    public IBrush FillBrush { get; set; } = Brushes.White;

    public IPen? Pen { get; set; }

    public IImageFilter? Filter
    {
        get
        {
            if (_filterStack.Count == 0)
                return null;

            return _filterStack.PeekOrDefault(default).ImageFilter;
        }
    }

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

    public void ClipPath(Geometry geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        _canvas.ClipPath(geometry.GetNativeObject(), operation.ToSKClipOperation());
    }

    public void Dispose()
    {
        void DisposeCore()
        {
            _surface.Dispose();
            _sharedFillPaint.Dispose();
            _sharedStrokePaint.Dispose();
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
        var size = new Size(bmp.Width, bmp.Height);
        ConfigureFillPaint(size);
        ConfigureStrokePaint(new Rect(size));

        if (bmp is Bitmap<Bgra8888>)
        {
            using var img = SKImage.FromPixels(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888), bmp.Data);

            _canvas.DrawImage(img, SKPoint.Empty, _sharedFillPaint);
        }
        else
        {
            using var skbmp = bmp.ToSKBitmap();
            _canvas.DrawBitmap(skbmp, SKPoint.Empty, _sharedFillPaint);
        }
    }

    public void DrawCircle(Rect rect)
    {
        VerifyAccess();
        ConfigureFillPaint(rect.Size);
        _canvas.DrawOval(rect.ToSKRect(), _sharedFillPaint);

        IPen? pen = Pen;
        if (pen != null && pen.Thickness != 0)
        {
            if (pen.StrokeAlignment == StrokeAlignment.Center)
            {
                ConfigureStrokePaint(rect);
                _canvas.DrawOval(rect.ToSKRect(), _sharedStrokePaint);
            }
            else
            {
                using (var path = new SKPath())
                {
                    path.AddOval(rect.ToSKRect());
                    DrawSKPath(path, true);
                }
            }
        }
    }

    public void DrawRect(Rect rect)
    {
        VerifyAccess();
        ConfigureFillPaint(rect.Size);
        _canvas.DrawRect(rect.ToSKRect(), _sharedFillPaint);

        IPen? pen = Pen;
        if (pen != null && pen.Thickness != 0)
        {
            if (pen.StrokeAlignment == StrokeAlignment.Center)
            {
                ConfigureStrokePaint(rect);
                _canvas.DrawRect(rect.ToSKRect(), _sharedStrokePaint);
            }
            else
            {
                using (var path = new SKPath())
                {
                    path.AddRect(rect.ToSKRect());
                    DrawSKPath(path, true);
                }
            }
        }
    }

    public void DrawText(FormattedText text)
    {
        VerifyAccess();
        var typeface = new Typeface(text.Font, text.Style, text.Weight);
        SKTypeface sktypeface = typeface.ToSkia();
        ConfigureFillPaint(text.Bounds);
        _sharedFillPaint.TextSize = text.Size;
        _sharedFillPaint.Typeface = sktypeface;
        _sharedFillPaint.Style = SKPaintStyle.Fill;
        Span<char> sc = stackalloc char[1];
        float prevRight = 0;

        foreach (char item in text.Text.AsSpan())
        {
            sc[0] = item;
            var bounds = default(SKRect);
            float w = _sharedFillPaint.MeasureText(sc, ref bounds);

            _canvas.Save();
            _canvas.Translate(prevRight + bounds.Left, 0);

            SKPath path = _sharedFillPaint.GetTextPath(
                sc,
                (bounds.Width / 2) - bounds.MidX,
                0/*-_paint.FontMetrics.Ascent*/);

            _canvas.DrawPath(path, _sharedFillPaint);
            path.Dispose();

            prevRight += text.Spacing;
            prevRight += w;

            _canvas.Restore();
        }
    }

    private void DrawSKPath(SKPath skPath, bool strokeOnly)
    {
        Rect rect = skPath.Bounds.ToGraphicsRect();

        if (!strokeOnly)
        {
            ConfigureFillPaint(rect.Size);
            _canvas.DrawPath(skPath, _sharedFillPaint);
        }

        IPen? pen = Pen;
        if (pen != null && pen.Thickness != 0)
        {
            ConfigureStrokePaint(rect);
            switch (pen.StrokeAlignment)
            {
                case StrokeAlignment.Center:
                    _canvas.DrawPath(skPath, _sharedStrokePaint);
                    break;

                case StrokeAlignment.Inside:
                    _canvas.Save();
                    _canvas.ClipPath(skPath, SKClipOperation.Intersect, true);
                    _canvas.DrawPath(skPath, _sharedStrokePaint);
                    _canvas.Restore();
                    break;

                case StrokeAlignment.Outside:
                    _canvas.Save();
                    _canvas.ClipPath(skPath, SKClipOperation.Difference, true);
                    _canvas.DrawPath(skPath, _sharedStrokePaint);
                    _canvas.Restore();
                    break;
            }
        }
    }

    public void DrawGeometry(Geometry geometry)
    {
        VerifyAccess();
        SKPath skPath = geometry.GetNativeObject();
        DrawSKPath(skPath, false);
    }

    public unsafe Bitmap<Bgra8888> GetBitmap()
    {
        VerifyAccess();
        var result = new Bitmap<Bgra8888>(Size.Width, Size.Height);

        _surface.ReadPixels(new SKImageInfo(Size.Width, Size.Height, SKColorType.Bgra8888), result.Data, result.Width * sizeof(Bgra8888), 0, 0);

        return result;
    }

    public PushedState PushPen(IPen? pen)
    {
        VerifyAccess();
        int level = _brushesStack.Count;
        _penStack.Push(Pen);
        Pen = pen;
        return new PushedState(this, level, PushedStateType.Pen);
    }

    public void PopPen(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _penStack.Count - 1 : level;

        while (_penStack.Count > level &&
            _penStack.TryPop(out IPen? state))
        {
            Pen = state;
        }
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
        ConfigurePaint(paint, bounds.Size, mask, (BlendMode)paint.BlendMode);
        _maskStack.Push(new MaskInfo(invert, paint));
        return new PushedState(this, level, PushedStateType.OpacityMask);
    }

    public void PopOpacityMask(int level = -1)
    {
        VerifyAccess();
        MaskInfo maskInfo = _maskStack.Pop();
        _sharedFillPaint.Reset();
        _sharedFillPaint.BlendMode = maskInfo.Invert ? SKBlendMode.DstOut : SKBlendMode.DstIn;

        _canvas.SaveLayer(_sharedFillPaint);
        using (SKPaint maskPaint = maskInfo.Paint)
        {
            _canvas.DrawPaint(maskPaint);
        }

        _canvas.Restore();

        _canvas.RestoreToCount(level);
    }

    public PushedState PushFillBrush(IBrush brush)
    {
        VerifyAccess();
        int level = _brushesStack.Count;
        _brushesStack.Push(FillBrush);
        FillBrush = brush;
        return new PushedState(this, level, PushedStateType.FillBrush);
    }

    public void PopFillBrush(int level = -1)
    {
        VerifyAccess();
        level = level < 0 ? _brushesStack.Count - 1 : level;

        while (_brushesStack.Count > level &&
            _brushesStack.TryPop(out IBrush? state))
        {
            FillBrush = state;
        }
    }

    public PushedState PushImageFilter(IImageFilter filter)
    {
        VerifyAccess();
        SKPaint? paint;
        using (var skFilter = filter.ToSKImageFilter())
        {
            paint = new SKPaint
            {
                ImageFilter = skFilter
            };
        }

        int level = _canvas.SaveLayer(paint);

        _filterStack.Push(new ImageFilterInfo(filter, paint, level));
        return new PushedState(this, level, PushedStateType.Filter);
    }

    public void PopImageFilter(int level = -1)
    {
        VerifyAccess();
        while (_filterStack.TryPop(out ImageFilterInfo state)
            && state.SaveCount >= level)
        {
            state.Paint.Dispose();
        }

        _canvas.RestoreToCount(level);
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

        using (SKShader shader = intermediate.ToShader(tileX, tileY, tileTransform))
        {
            paint.Shader = shader;
        }
    }

    private void ConfigureStrokePaint(Rect rect)
    {
        _sharedStrokePaint.Reset();
        IPen? pen = Pen;

        if (pen != null && pen.Thickness != 0)
        {
            float thickness = pen.Thickness;
            switch (pen.StrokeAlignment)
            {
                case StrokeAlignment.Center:
                    break;
                case StrokeAlignment.Inside:
                case StrokeAlignment.Outside:
                    thickness *= 2;
                    break;
                default:
                    break;
            }

            rect = rect.Inflate(thickness);
            _sharedStrokePaint.IsStroke = true;
            _sharedStrokePaint.StrokeWidth = thickness;
            _sharedStrokePaint.StrokeCap = (SKStrokeCap)pen.StrokeCap;
            _sharedStrokePaint.StrokeJoin = (SKStrokeJoin)pen.StrokeJoin;
            _sharedStrokePaint.StrokeMiter = pen.MiterLimit;
            if (pen.DashArray != null && pen.DashArray.Count > 0)
            {
                IReadOnlyList<float> srcDashes = pen.DashArray;

                int count = srcDashes.Count % 2 == 0 ? srcDashes.Count : srcDashes.Count * 2;

                float[] dashesArray = new float[count];

                for (int i = 0; i < count; ++i)
                {
                    dashesArray[i] = (float)srcDashes[i % srcDashes.Count] * thickness;
                }

                float offset = (float)(pen.DashOffset * thickness);

                var pe = SKPathEffect.CreateDash(dashesArray, offset);

                _sharedStrokePaint.PathEffect = pe;
            }

            ConfigurePaint(_sharedStrokePaint, rect.Size, pen.Brush, BlendMode);
        }
    }

    private void ConfigureFillPaint(Size targetSize)
    {
        _sharedFillPaint.Reset();
        ConfigurePaint(_sharedFillPaint, targetSize, FillBrush, BlendMode);
    }

    private static void ConfigurePaint(SKPaint paint, Size targetSize, IBrush? brush, BlendMode blendMode)
    {
        float opacity = brush?.Opacity ?? 0;
        paint.IsAntialias = true;
        paint.BlendMode = (SKBlendMode)blendMode;

        paint.Color = new SKColor(255, 255, 255, (byte)(255 * opacity));

        if (brush is ISolidColorBrush solid)
        {
            paint.Color = new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * opacity));
        }
        else if (brush is IGradientBrush gradient)
        {
            ConfigureGradientBrush(paint, targetSize, gradient);
        }
        else if (brush is ITileBrush tileBrush)
        {
            ConfigureTileBrush(paint, targetSize, tileBrush);
        }
        else
        {
            paint.Color = new SKColor(255, 255, 255, 0);
        }
    }

    private readonly record struct MaskInfo(bool Invert, SKPaint Paint);

    private readonly record struct ImageFilterInfo(IImageFilter ImageFilter, SKPaint Paint, int SaveCount);

    private readonly record struct PenInfo(IPen? Pen, SKPaint? Paint, int SaveCount);

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
