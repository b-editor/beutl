using BEditorNext.Media;
using BEditorNext.Media.Pixel;

using SkiaSharp;

namespace BEditorNext.Graphics.Effects;

public class DropShadow : BitmapEffect
{
    public float X { get; set; }

    public float Y { get; set; }

    public float SigmaX { get; set; }

    public float SigmaY { get; set; }

    public Color Color { get; set; }

    public bool ShadowOnly { get; set; }

    public override Rect Measure(Rect rect)
{
        float w = rect.Width + SigmaX;
        float h = rect.Height + SigmaY;

        if (!ShadowOnly)
        {
            w += Math.Abs(X);
            h += Math.Abs(Y);

            return rect.WithX(rect.X + Math.Min(X, 0))
                .WithY(rect.Y + Math.Min(Y, 0))
                .WithWidth(w)
                .WithHeight(h);
        }
        else
        {
            return rect.WithX(rect.X + X)
                .WithY(rect.Y + Y)
                .WithWidth(w)
                .WithHeight(h);
        }
    }

    public override void Apply(ref Bitmap<Bgra8888> bitmap)
    {
        float w = bitmap.Width + SigmaX;
        float h = bitmap.Height + SigmaY;

        if (ShadowOnly)
        {
            using var filter = SKImageFilter.CreateDropShadowOnly(0, 0, SigmaX, SigmaY, Color.ToSkia());
            using var paint = new SKPaint
            {
                ImageFilter = filter,
                IsAntialias = true,
            };

            using var bmp1 = bitmap.ToSKBitmap();
            using var bmp2 = new SKBitmap((int)w, (int)h);
            using var canvas = new SKCanvas(bmp2);

            canvas.DrawBitmap(bmp1, 0, 0, paint);

            bitmap.Dispose();
            bitmap = bmp2.ToBitmap();
        }
        else
        {
            // キャンバスのサイズ
            float size_w = Math.Abs(X) + w;
            float size_h = Math.Abs(Y) + h;

            using var filter = SKImageFilter.CreateDropShadow(X, Y, SigmaX, SigmaY, Color.ToSkia());
            using var paint = new SKPaint
            {
                ImageFilter = filter,
                IsAntialias = true,
            };

            using var bmp1 = bitmap.ToSKBitmap();
            using var bmp2 = new SKBitmap((int)size_w, (int)size_h);
            using var canvas = new SKCanvas(bmp2);

            canvas.DrawBitmap(
                bmp1,
                Math.Abs(Math.Min(X, 0)),
                Math.Abs(Math.Min(Y, 0)),
                paint);

            bitmap.Dispose();
            bitmap = bmp2.ToBitmap();
        }
    }
}
