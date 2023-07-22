using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Rendering;

namespace Beutl.Graphics;

public interface IDrawable : IRenderable
{
    Rect Bounds { get; }

    IBrush? Foreground { get; set; }

    BlendMode BlendMode { get; set; }

    ITransform? Transform { get; set; }

    FilterEffect? FilterEffect { get; set; }

    AlignmentX AlignmentX { get; set; }

    AlignmentY AlignmentY { get; set; }

    RelativePoint TransformOrigin { get; set; }

    IBitmap ToBitmap();
}
