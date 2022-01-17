using BeUtl.Media;

namespace BeUtl.Graphics;

public sealed class DrawableBitmap : Drawable
{
    public DrawableBitmap()
    {
    }

    public DrawableBitmap(IBitmap bitmap)
    {
        Bitmap = bitmap;
    }

    ~DrawableBitmap()
    {
        Dispose();
    }

    public IBitmap? Bitmap { get; private set; }

    public void Initialize(IBitmap bitmap)
    {
        Bitmap?.Dispose();
        Bitmap = bitmap;
        GC.ReRegisterForFinalize(this);
        IsDisposed = false;

        Initialize();
    }

    public override void Dispose()
    {
        if (!IsDisposed)
        {
            Bitmap?.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (!IsDisposed && Bitmap is not null)
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
