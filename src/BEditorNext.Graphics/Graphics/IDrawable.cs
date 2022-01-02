using System.Numerics;

using BEditorNext.Graphics.Transformation;
using BEditorNext.Media;

namespace BEditorNext.Graphics;

public interface IDrawable : IDisposable
{
    PixelSize Size { get; }

    Color Foreground { get; set; }

    bool IsAntialias { get; set; }

    Transforms Transform { get; }

    AlignmentX HorizontalAlignment { get; set; }

    AlignmentY VerticalAlignment { get; set; }

    AlignmentX HorizontalContentAlignment { get; set; }

    AlignmentY VerticalContentAlignment { get; set; }

    EffectCollection Effects { get; }

    void Draw(ICanvas canvas);
}
