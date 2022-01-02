using BEditorNext.Graphics.Transformation;
using BEditorNext.Media;
using BEditorNext.Rendering;

namespace BEditorNext.Graphics;

public interface IDrawable : IDisposable, IRenderable
{
    PixelSize Size { get; }

    Color Foreground { get; set; }

    bool IsAntialias { get; set; }

    BlendMode BlendMode { get; set; }

    Transforms Transform { get; }

    AlignmentX HorizontalAlignment { get; set; }

    AlignmentY VerticalAlignment { get; set; }

    AlignmentX HorizontalContentAlignment { get; set; }

    AlignmentY VerticalContentAlignment { get; set; }

    EffectCollection Effects { get; }

    void Draw(ICanvas canvas);

    void IRenderable.Render(IRenderer renderer)
    {
        Draw(renderer.Graphics);
    }
}
