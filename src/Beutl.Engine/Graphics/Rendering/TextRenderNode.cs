using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class TextRenderNode(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public FormattedText Text { get; private set; } = text;

    public bool Update(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        var oldText = Text;
        Text = text;
        if (changed || !oldText.Equals(text))
        {
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(Text.ActualBounds, canvas => canvas.DrawText(Text, Fill?.Resource, Pen?.Resource), HitTest)
        ];
    }

    private bool HitTest(Point point)
    {
        SKPath fill = Text.GetFillPath();
        if (Fill != null && fill.Contains(point.X, point.Y))
        {
            return true;
        }

        SKPath? stroke = Text.GetStrokePath();
        return stroke?.Contains(point.X, point.Y) == true;
    }
}
