using System.Numerics;

using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public class RenderableBitmap : IRenderableBitmap
{
    public RenderableBitmap(Bitmap<Bgra8888> bitmap)
    {
        Bitmap = bitmap;
    }

    ~RenderableBitmap()
    {
        Bitmap.Dispose();
    }

    public Bitmap<Bgra8888> Bitmap { get; }

    public bool IsDisposed => Bitmap.IsDisposed;

    public (AlignmentX X, AlignmentY Y) Alignment { get; set; }

    public PixelSize Size => new(Bitmap.Width, Bitmap.Height);

    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;

    public IList<BitmapEffect> Effects { get; } = new List<BitmapEffect>();

    public Dictionary<string, object> Options { get; } = new();

    public void Dispose()
    {
        Bitmap.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Render(IRenderer renderer)
    {
        if (!IsDisposed)
        {
            using Bitmap<Bgra8888> bitmap = ToBitmap();
            using Bitmap<Bgra8888> bitmap2 = BitmapEffect.ApplyAll(bitmap, Effects);

            renderer.Graphics.PushMatrix();

            renderer.Graphics.SetMatrix(Transform * renderer.Graphics.TotalMatrix);

            Point pt = CreatePoint(bitmap2.Width, bitmap2.Height);
            renderer.Graphics.Translate(pt);
            renderer.Graphics.DrawBitmap(bitmap2);

            renderer.Graphics.PopMatrix();
        }
    }

    public Bitmap<Bgra8888> ToBitmap()
    {
        return (Bitmap<Bgra8888>)Bitmap.Clone();
    }

    private Point CreatePoint(float width, float height)
    {
        float x = 0;
        float y = 0;

        if (Alignment.X == AlignmentX.Center)
        {
            x -= width / 2;
        }
        else if (Alignment.X == AlignmentX.Right)
        {
            x -= width;
        }

        if (Alignment.Y == AlignmentY.Center)
        {
            y -= height / 2;
        }
        else if (Alignment.Y == AlignmentY.Bottom)
        {
            y -= height;
        }

        return new Point(x, y);
    }
}
