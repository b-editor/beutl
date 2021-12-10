using System.Numerics;

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
            renderer.Graphics.DrawBitmap(Bitmap);

            renderer.Graphics.PopMatrix();
        }
    }
}
