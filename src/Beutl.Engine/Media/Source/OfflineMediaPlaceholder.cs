using SkiaSharp;

namespace Beutl.Media.Source;

internal static class OfflineMediaPlaceholder
{
    public static PixelSize Size => new(320, 180);

    public static Bitmap CreateBitmap()
    {
        var bitmap = new SKBitmap(Size.Width, Size.Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(18, 18, 18));
        using var paint = new SKPaint { Color = new SKColor(205, 45, 45), StrokeWidth = 6, IsAntialias = true };
        canvas.DrawLine(110, 40, 210, 140, paint);
        canvas.DrawLine(210, 40, 110, 140, paint);
        paint.Style = SKPaintStyle.Stroke;
        canvas.DrawRect(3, 3, Size.Width - 6, Size.Height - 6, paint);
        return new Bitmap(bitmap);
    }
}
