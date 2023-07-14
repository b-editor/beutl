using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class BitmapNode : BrushDrawNode
{
    public BitmapNode(IBitmap bitmap, IBrush? fill, IPen? pen)
        : base(fill, pen, PenHelper.GetBounds(new Rect(0, 0, bitmap.Width, bitmap.Height), pen))
    {
        Bitmap = bitmap;
    }

    public IBitmap Bitmap { get; }

    public bool Equals(IBitmap bitmap, IBrush? fill, IPen? pen)
    {
        return Bitmap == bitmap
            && Fill == fill
            && Pen == pen;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.DrawBitmap(Bitmap, Fill, Pen);
    }

    public override void Dispose()
    {
        Bitmap.Dispose();
    }

    public override bool HitTest(Point point)
    {
        StrokeAlignment alignment = Pen?.StrokeAlignment ?? StrokeAlignment.Inside;
        float thickness = Pen?.Thickness ?? 0;
        thickness = PenHelper.GetRealThickness(alignment, thickness);

        if (Fill != null)
        {
            Rect rect = Bounds.Inflate(thickness);
            return rect.ContainsExclusive(point);
        }
        else
        {
            Rect borderRect = Bounds.Inflate(thickness);
            Rect emptyRect = Bounds.Deflate(thickness);
            return borderRect.ContainsExclusive(point) && !emptyRect.ContainsExclusive(point);
        }
    }
}
