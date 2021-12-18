using System.Numerics;

using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public class RenderableList : List<IRenderable>
{
}

public class RenderableBitmap : IRenderable
{
    private Matrix3x2 _matrix = Matrix3x2.Identity;

    public RenderableBitmap(Bitmap<Bgra8888> bitmap)
    {
        Bitmap = bitmap;
    }

    ~RenderableBitmap()
    {
        Bitmap.Dispose();
    }

    public Bitmap<Bgra8888> Bitmap { get; }

    public ref Matrix3x2 Transform => ref _matrix;

    public (AlignmentX X, AlignmentY Y) Alignment { get; set; }

    public bool IsDisposed => Bitmap.IsDisposed;

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
            renderer.Graphics.PushMatrix();

            renderer.Graphics.SetMatrix(Transform * renderer.Graphics.TotalMatrix);

            Point pt = CreatePoint(Bitmap.Width, Bitmap.Height);
            renderer.Graphics.Translate(pt);
            renderer.Graphics.DrawBitmap(Bitmap);

            renderer.Graphics.PopMatrix();
        }
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

        if (Alignment.Y== AlignmentY.Center)
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
