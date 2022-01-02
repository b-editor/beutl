using System.Numerics;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.Media.TextFormatting;

namespace BEditorNext.Graphics;

public interface ICanvas : IDisposable
{
    PixelSize Size { get; }

    bool IsDisposed { get; }

    Color Color { get; set; }

    float StrokeWidth { get; set; }

    bool IsAntialias { get; set; }

    BlendMode BlendMode { get; set; }

    Matrix3x2 TotalMatrix { get; }

    void Clear();

    void Clear(Color color);

    void DrawBitmap(Bitmap<Bgra8888> bmp);

    void DrawCircle(Size size);

    void DrawRect(Size size);

    void FillCircle(Size size);

    void FillRect(Size size);

    void DrawText(TextElement text);

    Bitmap<Bgra8888> GetBitmap();

    CanvasAutoRestore PushState();

    void PopState(int count = -1);

    void ResetMatrix();

    void RotateDegrees(float degrees);

    void RotateRadians(float radians);

    void Scale(Vector2 vector);

    void Skew(Vector2 vector);

    void Translate(Vector2 vector);

    void SetMatrix(Matrix3x2 matrix);
}
