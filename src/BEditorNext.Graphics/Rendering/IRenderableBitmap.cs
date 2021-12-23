using System.Numerics;

using BEditorNext.Graphics.Effects;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public interface IRenderableBitmap : IRenderable
{
    PixelSize Size { get; }

    Matrix3x2 Transform { get; set; }

    (AlignmentX X, AlignmentY Y) Alignment { get; set; }

    IList<BitmapEffect> Effects { get; }

    Bitmap<Bgra8888> ToBitmap();
}
