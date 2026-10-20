using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public sealed class GraphEditorScale : GraphEditorRulerControl
{
    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);

    public GraphEditorScale()
        : base("GraphEditorScaleTextBrush")
    {
        ClipToBounds = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double step = GraphEditorGridMetrics.Step(Scale);
        using (context.PushClip(new Rect(Bounds.Size)))
            foreach (var (value, pixel) in MajorTicks(Offset.Y, Offset.Y + Bounds.Height))
            {
                double y = pixel - Offset.Y;
                if (y >= 0 && y <= Bounds.Height)
                {
                    context.DrawLine(Pen, new(Bounds.Width - 5, y), new(Bounds.Width, y));
                    string label = Math.Abs(value) < step * 0.0001 ? "0" : value.ToString("G4", CultureInfo.CurrentCulture);
                    using var text = new TextLayout(label, s_typeface, 13, Brush);
                    text.Draw(context, new(Math.Max(2, Bounds.Width - text.Width - 9), y - text.Height / 2));
                }
                foreach (double yy in MinorTicks(y))
                {
                    if (yy >= 0 && yy <= Bounds.Height)
                        context.DrawLine(Pen, new(Bounds.Width - 3, yy), new(Bounds.Width, yy));
                }
            }
    }
}
