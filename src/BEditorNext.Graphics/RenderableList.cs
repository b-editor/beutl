using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BEditorNext.Graphics;
using BEditorNext.Graphics.Pixel;

namespace BEditorNext;

public class RenderableList : List<IRenderable>
{
}

public class RenderableBitmap : IRenderable
{
    public RenderableBitmap(Bitmap<Bgra8888> bitmap)
    {
        Bitmap = bitmap;
    }

    public Bitmap<Bgra8888> Bitmap { get; }

    public bool IsDisposed => Bitmap.IsDisposed;

    public Dictionary<string, object> Options { get; } = new();

    public void Dispose()
    {
        Bitmap.Dispose();
    }

    public void Render(IRenderer renderer)
    {
        if (!IsDisposed)
        {
            renderer.Graphics.DrawBitmap(Bitmap);
        }
    }
}