using BEditorNext.Graphics.Pixel;

using SkiaSharp;

namespace BEditorNext.Graphics.Effects;

public class DropShadow : IEffect
{
    public float X { get; set; }

    public float Y { get; set; }

    public float SigmaX { get; set; }

    public float SigmaY { get; set; }

    public Color Color { get; set; }

    public bool ShadowOnly { get; set; }

    public Bitmap<Bgra8888> Apply(Bitmap<Bgra8888> bitmap)
    {
        var w = bitmap.Width + SigmaX;
        var h = bitmap.Height + SigmaY;

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

            return bmp2.ToBitmap();
        }
        else
        {
            // キャンバスのサイズ
            var size_w = (Math.Abs(X) + (w / 2)) * 2;
            var size_h = (Math.Abs(Y) + (h / 2)) * 2;

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

            return bmp2.ToBitmap();
        }
    }
}
