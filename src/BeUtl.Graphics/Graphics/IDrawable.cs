using BeUtl.Graphics.Filters;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.Rendering;

namespace BeUtl.Graphics;

public interface IDrawable : IRenderable
{
    float Width { get; set; }

    float Height { get; set; }

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
