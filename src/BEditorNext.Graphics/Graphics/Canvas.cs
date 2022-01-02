using System.Numerics;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.Media.TextFormatting;
using BEditorNext.Threading;

using SkiaSharp;

namespace BEditorNext.Graphics;

public class Canvas : ICanvas
{
    private readonly SKSurface _surface;
    private readonly SKCanvas _canvas;
    private readonly SKPaint _paint;
    private readonly Dispatcher? _dispatcher;

    public Canvas(int width, int height)
    {
        _dispatcher = Dispatcher.Current;
        Size = new PixelSize(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        _surface = SKSurface.Create(info);

        _canvas = _surface.Canvas;
        _paint = new SKPaint();
        IsAntialias = true;
        _paint.BlendMode = SKBlendMode.SrcOver;

        ResetMatrix();
    }

    ~Canvas()
    {
        Dispose();
    }

    public bool IsDisposed { get; private set; }

    public Color Color
    {
        get => _paint.Color.ToColor();
        set => _paint.Color = value.ToSkia();
    }

    public float StrokeWidth { get; set; }

    public bool IsAntialias
    {
        get => _paint.IsAntialias;
        set => _paint.IsAntialias = value;
    }

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
        using var img = SKImage.FromPixels(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888), bmp.Data);

        _canvas.DrawImage(img, SKPoint.Empty, _paint);
    }

    public void DrawCircle(Size size)
    {
        VerifyAccess();
        float line = StrokeWidth;

        if (line >= MathF.Min(size.Width, size.Height) / 2)
            line = MathF.Min(size.Width, size.Height) / 2;

        float min = MathF.Min(size.Width, size.Height);

        if (line < min) min = line;
        if (min < 0) min = 0;

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = min;

        _canvas.DrawOval(
            new SKPoint(0, 0),
            new SKSize((size.Width - min) / 2, (size.Height - min) / 2),
            _paint);

        _paint.Style = SKPaintStyle.Fill;
    }

    public void DrawRect(Size size)
    {
        VerifyAccess();
        float line = StrokeWidth;

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = line;

        _canvas.DrawRect(
            (line - size.Width) / 2, (line - size.Height) / 2,
            size.Width - line, size.Height - line,
            _paint);

        _paint.Style = SKPaintStyle.Fill;
    }

    public void FillCircle(Size size)
    {
        VerifyAccess();
        _paint.Style = SKPaintStyle.Fill;

        _canvas.DrawOval(SKPoint.Empty, size.ToSkia(), _paint);
    }

    public void FillRect(Size size)
    {
        VerifyAccess();
        _paint.Style = SKPaintStyle.Fill;

        _canvas.DrawRect(0, 0, size.Width, size.Height, _paint);
    }

    public unsafe void DrawVertices(VertexMode vmode, Point[] vertices, Point[] texs, Color[] colors, Bitmap<Bgra8888>? bmp = null)
    {
        VerifyAccess();
        static SKPoint[] ToSKPoints(Point[] vectors)
        {
            var array = new SKPoint[vectors.Length];
            for (int i = 0; i < vectors.Length; i++)
            {
                array[i] = vectors[i].ToSkia();
            }

            return array;
        }

        static SKColor[] ToSKColors(Color[] colors)
        {
            var array = new SKColor[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                array[i] = colors[i].ToSkia();
            }

            return array;
        }

        if (bmp != null)
        {
            using var skbmp = new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888));
            skbmp.SetPixels(bmp.Data);
            using var shader = SKShader.CreateBitmap(skbmp);
            _paint.Shader = shader;

            _canvas.DrawVertices((SKVertexMode)vmode, ToSKPoints(vertices), ToSKPoints(texs), ToSKColors(colors), _paint);
        }
        else
        {
            _canvas.DrawVertices((SKVertexMode)vmode, ToSKPoints(vertices), ToSKPoints(texs), ToSKColors(colors), _paint);
        }
    }

    // Marginを考慮しない
    public void DrawText(TextElement text)
    {
        VerifyAccess();
        _paint.TextSize = text.Size;
        _paint.Typeface = text.Typeface.ToSkia();
        _paint.Color = text.Color.ToSkia();
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

    public void PushMatrix()
    {
        VerifyAccess();
        _canvas.Save();
    }

    public void PopMatrix()
    {
        VerifyAccess();
        _canvas.Restore();
    }

    private void VerifyAccess()
    {
        _dispatcher?.VerifyAccess();
    }
}
