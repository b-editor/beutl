using System.Numerics;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.Media.TextFormatting;

using SkiaSharp;

namespace BEditorNext.Graphics;

public interface IGraphics : IDisposable
{
    public PixelSize Size { get; }

    public bool IsDisposed { get; }

    public Color Color { get; set; }

    public float StrokeWidth { get; set; }

    public bool IsAntialias { get; set; }

    public Matrix3x2 TotalMatrix { get; }

    public void Clear();

    public void Clear(Color color);

    public void DrawBitmap(Bitmap<Bgra8888> bmp);

    public void DrawCircle(Size size);

    public void DrawRect(Size size);

    public void FillCircle(Size size);

    public void FillRect(Size size);

    public void DrawVertices(VertexMode vmode, Point[] vertices, Point[] texs, Color[] colors, Bitmap<Bgra8888>? bmp = null);

    public void DrawText(TextElement text);

    public Bitmap<Bgra8888> GetBitmap();

    public void PushMatrix();

    public void PopMatrix();

    public void ResetMatrix();

    public void RotateDegrees(float degrees);

    public void RotateRadians(float radians);

    public void Scale(Vector2 vector);

    public void Skew(Vector2 vector);

    public void Translate(Vector2 vector);

    public void SetMatrix(Matrix3x2 matrix);
}
