using Beutl.Graphics.Filters;
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

    IImageFilter? Filter { get; set; }

    AlignmentX AlignmentX { get; set; }

    AlignmentY AlignmentY { get; set; }

    RelativePoint TransformOrigin { get; set; }

    void Draw(ICanvas canvas);

    IBitmap ToBitmap();

    void IRenderable.Render(IRenderer renderer)
    {
        Draw(renderer.Graphics);
    }
}
