using BeUtl.Graphics.Filters;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.Rendering;

namespace BeUtl.Graphics;

public interface IDrawable : IDisposable, IRenderable
{
    float Width { get; set; }

    float Height { get; set; }

    Rect Bounds { get; }

    IBrush Foreground { get; set; }

    BlendMode BlendMode { get; set; }

    Transforms Transform { get; }

    AlignmentX HorizontalAlignment { get; set; }

    AlignmentY VerticalAlignment { get; set; }

    AlignmentX HorizontalContentAlignment { get; set; }

    AlignmentY VerticalContentAlignment { get; set; }

    ImageFilters Filters { get; }

    void Draw(ICanvas canvas);

    IBitmap ToBitmap();

    void IRenderable.Render(IRenderer renderer)
    {
        Draw(renderer.Graphics);
    }
}
