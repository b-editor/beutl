using System.Numerics;

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
    Color Color,
    float StrokeWidth,
    bool IsAntialias,
    Matrix3x2 Matrix,
    SKBlendMode BlendMode)
{
    public CanvasState()
        : this(Colors.White, 0, true, Matrix3x2.Identity, SKBlendMode.SrcOver)
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

    public Color Color { get; set; } = Colors.White;

    public float StrokeWidth { get; set; }

    public bool IsAntialias { get; set; } = true;

    public SKBlendMode BlendMode { get; set; } = SKBlendMode.SrcOver;

    public PixelSize Size { get; }

    public Matrix3x2 TotalMatrix => _canvas.TotalMatrix.ToMatrix3x2();

    public void Clear()
    {
        VerifyAccess();
        _canvas.Clear();
    }

    public void Clear(Color color)
    {
        VerifyAccess();
        _canvas.Clear(color.ToSkia());
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
        ApplyState();
        using var img = SKImage.FromPixels(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888), bmp.Data);

        _canvas.DrawImage(img, SKPoint.Empty, _paint);
    }

    public void DrawCircle(Size size)
    {
        VerifyAccess();
        ApplyState();
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
        ApplyState();
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
        ApplyState();
        _paint.Style = SKPaintStyle.Fill;

        _canvas.DrawOval(SKPoint.Empty, size.ToSkia(), _paint);
    }

    public void FillRect(Size size)
    {
        VerifyAccess();
        ApplyState();

        _paint.Style = SKPaintStyle.Fill;

        _canvas.DrawRect(0, 0, size.Width, size.Height, _paint);
    }

    // Marginを考慮しない
    public void DrawText(TextElement text)
    {
        VerifyAccess();
        ApplyState();
        _paint.TextSize = text.Size;
        _paint.Typeface = text.Typeface.ToSkia();
        _paint.Color = text.Color.ToSkia();
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

    public void Scale(Vector2 vector)
    {
        VerifyAccess();
        _canvas.Scale(vector.X, vector.Y);
    }

    public void SetMatrix(Matrix3x2 matrix)
    {
        VerifyAccess();
        _canvas.SetMatrix(matrix.ToSKMatrix());
    }

    public void Skew(Vector2 vector)
    {
        VerifyAccess();
        _canvas.Skew(vector.X, vector.Y);
    }

    public void Translate(Vector2 vector)
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
        return new CanvasState(Color, StrokeWidth, IsAntialias, TotalMatrix, BlendMode);
    }

    private void SetState(CanvasState state)
    {
        Color = state.Color;
        StrokeWidth = state.StrokeWidth;
        IsAntialias = state.IsAntialias;
        BlendMode = state.BlendMode;

        //_canvas.SetMatrix(state.Matrix.ToSKMatrix());
    }

    private void ApplyState()
    {
        _paint.Color = Color.ToSkia();
        _paint.StrokeWidth = StrokeWidth;
        _paint.IsAntialias = IsAntialias;
        _paint.BlendMode = BlendMode;
    }
}
