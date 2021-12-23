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
            float size_w = (Math.Abs(X) + (w / 2)) * 2;
            float size_h = (Math.Abs(Y) + (h / 2)) * 2;

            using var filter = SKImageFilter.CreateDropShadow(0, 0, SigmaX, SigmaY, Color.ToSkia());
            using var paint = new SKPaint
            {
                ImageFilter = filter,
                IsAntialias = true,
            };

            using var bmp1 = bitmap.ToSKBitmap();
            using var bmp2 = new SKBitmap((int)w, (int)h);
            using var canvas = new SKCanvas(bmp2);

            canvas.DrawBitmap(
                bmp1,
                (size_w / 2) - (bitmap.Width / 2),
                (size_h / 2) - (bitmap.Height / 2),
                paint);

            bitmap.Dispose();
            bitmap = bmp2.ToBitmap();
        }
    }
}
