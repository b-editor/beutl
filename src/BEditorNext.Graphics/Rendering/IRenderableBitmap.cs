using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public interface IRenderableBitmap : IRenderable, IDrawable
{
    Bitmap<Bgra8888> ToBitmap();
}
