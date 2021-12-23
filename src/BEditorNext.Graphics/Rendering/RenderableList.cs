using System.Numerics;

using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public class RenderableList : List<IRenderable>
{
}

public interface IRenderableBitmap : IRenderable
{
    PixelSize Size { get; }

    Matrix3x2 Transform { get; set; }

    (AlignmentX X, AlignmentY Y) Alignment { get; set; }

    IList<IEffect> Effects { get; }
}

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

    public IList<IEffect> Effects { get; } = new List<IEffect>();

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
