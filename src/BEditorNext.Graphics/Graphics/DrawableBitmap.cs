
using BEditorNext.Graphics.Effects;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics;

public sealed class DrawableBitmap : Drawable
{
    public DrawableBitmap(Bitmap<Bgra8888> bitmap)
    {
        Bitmap = bitmap;
    }

    ~DrawableBitmap()
    {
        Dispose();
    }

    public override PixelSize Size => new(Bitmap.Width, Bitmap.Height);

    public Bitmap<Bgra8888> Bitmap { get; private set; }

    public void Initialize(Bitmap<Bgra8888> bitmap)
    {
        Bitmap.Dispose();
        Bitmap = bitmap;
        GC.ReRegisterForFinalize(this);
        IsDisposed = false;

        Initialize();
    }

    public override void Dispose()
    {
        if (!IsDisposed)
        {
            Bitmap.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (!IsDisposed)
        {
            if (Effects.Count == 0)
            {
                canvas.DrawBitmap(Bitmap);
            }
            else
            {
                using Bitmap<Bgra8888> bitmap = (Bitmap<Bgra8888>)Bitmap.Clone();
                using Bitmap<Bgra8888> bitmap2 = BitmapEffect.ApplyAll(bitmap, Effects);

                canvas.DrawBitmap(bitmap2);
            }
        }
    }
}
