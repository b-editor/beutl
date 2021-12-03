using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public class RenderableList : List<IRenderable>
{
}

public class RenderableBitmap : IRenderable
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
            renderer.Graphics.DrawBitmap(Bitmap);
        }
    }
}
