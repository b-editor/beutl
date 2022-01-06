using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.Media.TextFormatting;
using BEditorNext.Threading;

using SkiaSharp;

namespace BEditorNext.Graphics;

public readonly struct CanvasAutoRestore : IDisposable
{
    public CanvasAutoRestore(ICanvas canvas, int count)
    {
        Canvas = canvas;
        Count = count;
    }

    public ICanvas Canvas { get; }

    public int Count { get; }

    public void Dispose()
    {
        Canvas.PopState(Count);
    }
}

public readonly record struct CanvasState(
    IBrush Foreground,
    float StrokeWidth,
    bool IsAntialias,
    Matrix Matrix,
    BlendMode BlendMode)
{
    public CanvasState()
        : this(Brushes.Transparent, 0, true, Matrix.Identity, BlendMode.SrcOver)
    {

    }
}

public class Canvas : ICanvas
{
    private readonly SKSurface _surface;
    private readonly SKCanvas _canvas;
    private readonly SKPaint _paint;
    private readonly Dispatcher? _dispatcher;
    private readonly Stack<CanvasState> _stack = new();

    public Canvas(int width, int height)
    {
        _dispatcher = Dispatcher.Current;
        Size = new PixelSize(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        _surface = SKSurface.Create(info);

        _canvas = _surface.Canvas;
        _paint = new SKPaint();

        _stack.Push(GetState());

        ResetMatrix();
    }

    ~Canvas()
    {
        Dispose();
    }

    public bool IsDisposed { get; private set; }

    public IBrush Foreground { get; set; } = Brushes.White;

    public float StrokeWidth { get; set; }

    public bool IsAntialias { get; set; } = true;

    public BlendMode BlendMode { get; set; } = BlendMode.SrcOver;

    public PixelSize Size { get; }

    public Matrix TotalMatrix => _canvas.TotalMatrix.ToMatrix();

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

    public void DrawBitmap(Bitmap<Bgra8888> bmp)
    {
        VerifyAccess();
        ConfigurePaint(_paint, new Size(bmp.Width, bmp.Height));
        using var img = SKImage.FromPixels(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888), bmp.Data);

        _canvas.DrawImage(img, SKPoint.Empty, _paint);
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

    public void ResetMatrix()
    {
        VerifyAccess();
        _canvas.ResetMatrix();
    }

    public void RotateDegrees(float degrees)
    {
        VerifyAccess();
        _canvas.RotateDegrees(degrees);
    }

    public void RotateRadians(float radians)
    {
        VerifyAccess();
        _canvas.RotateRadians(radians);
    }

    public void Scale(Vector vector)
    {
        VerifyAccess();
        _canvas.Scale(vector.X, vector.Y);
    }

    public void SetMatrix(Matrix matrix)
    {
        VerifyAccess();
        _canvas.SetMatrix(matrix.ToSKMatrix());
    }

    public void Skew(Vector vector)
    {
        VerifyAccess();
        _canvas.Skew(vector.X, vector.Y);
    }

    public void Translate(Vector vector)
    {
        VerifyAccess();
        _canvas.Translate(vector.X, vector.Y);
    }

    public CanvasAutoRestore PushState()
    {
        VerifyAccess();
        int count = _canvas.Save();

        _stack.Push(GetState());

        return new CanvasAutoRestore(this, count);
    }

    public void PopState(int count = -1)
    {
        VerifyAccess();

        if (count < 0)
        {
            _canvas.Restore();

            if (_stack.TryPop(out CanvasState state))
            {
                SetState(state);
            }
        }
        else
        {
            _canvas.RestoreToCount(count);

            while (_stack.TryPop(out CanvasState state))
            {
                if (_stack.Count == count)
                {
                    SetState(state);
                    break;
                }
            }
        }
    }

    private void VerifyAccess()
    {
        _dispatcher?.VerifyAccess();
    }

    private CanvasState GetState()
    {
        return new CanvasState(Foreground, StrokeWidth, IsAntialias, TotalMatrix, BlendMode);
    }

    private void SetState(CanvasState state)
    {
        Foreground = state.Foreground;
        StrokeWidth = state.StrokeWidth;
        IsAntialias = state.IsAntialias;
        BlendMode = state.BlendMode;

        //_canvas.SetMatrix(state.Matrix.ToSKMatrix());
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
        else if (tileBrush is IImageBrush { Source: { IsDisposed: false} } imageBrush)
        {
            using var bmp = imageBrush.Source.ToSKBitmap();
            using var shader = SKShader.CreateBitmap(bmp, tileX, tileY, paintTransform);

            paint.Shader = shader;
        }
    }

    private void ConfigurePaint(SKPaint paint, Size targetSize)
    {
        double opacity = Foreground.Opacity;
        paint.StrokeWidth = StrokeWidth;
        paint.IsAntialias = IsAntialias;
        paint.BlendMode = (SKBlendMode)BlendMode;

        if (Foreground is ISolidColorBrush solid)
        {
            paint.Color = new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * opacity));

            return;
        }

        paint.Color = new SKColor(255, 255, 255, (byte)(255 * opacity));

        if (Foreground is IGradientBrush gradient)
        {
            ConfigureGradientBrush(paint, targetSize, gradient);

            return;
        }

        if (Foreground is ITileBrush tileBrush)
        {
            ConfigureTileBrush(paint, targetSize, tileBrush);

            return;
        }
        else
        {
            paint.Color = new SKColor(255, 255, 255, 0);
        }
    }
}
