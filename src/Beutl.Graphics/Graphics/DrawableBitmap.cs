using Beutl.Media;

namespace Beutl.Graphics;

public sealed class DrawableBitmap : Drawable
{
    public DrawableBitmap()
    {
    }

    public DrawableBitmap(IBitmap bitmap)
    {
        Bitmap = bitmap;
    }

    public IBitmap? Bitmap { get; set; }

    protected override void OnDraw(ICanvas canvas)
    {
        if (Bitmap?.IsDisposed == false)
        {
            if (Width > 0 && Height > 0)
            {
                using (canvas.PushTransform(Matrix.CreateScale(Width / Bitmap.Width, Height / Bitmap.Height)))
                {
                    canvas.DrawBitmap(Bitmap);
                }
            }
            else
            {
                canvas.DrawBitmap(Bitmap);
            }
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        return new(Bitmap?.Width ?? 0, Bitmap?.Height ?? 0);
    }
}
