using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Rendering;

// Todo: bounds,HitTest
public sealed class TextNode(FormattedText text, IBrush? fill, IPen? pen)
    : BrushDrawNode(fill, pen, new(new Point(0, text.Metrics.Ascent), text.Bounds))
{
    public FormattedText Text { get; private set; } = text;

    public bool Equals(FormattedText text, IBrush? fill, IPen? pen)
    {
        return Text == text
            && EqualityComparer<IBrush?>.Default.Equals(Fill, fill)
            && EqualityComparer<IPen?>.Default.Equals(Pen, pen);
    }

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.DrawText(Text, Fill, Pen);
    }

    public override bool HitTest(Point point)
    {
        return Bounds.ContainsExclusive(point);
    }
}
