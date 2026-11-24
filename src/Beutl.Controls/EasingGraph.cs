using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

using Beutl.Animation.Easings;

namespace Beutl.Controls;

public sealed partial class EasingGraph : TemplatedControl
{
    private readonly Pen _pen = new()
    {
        LineJoin = PenLineJoin.Round,
        LineCap = PenLineCap.Round,
    };

    static EasingGraph()
    {
        AffectsRender<EasingGraph>(EasingProperty, StrokeThicknessProperty);
    }

    [GeneratedStyledProperty]
    public partial Easing? Easing { get; set; }

    [GeneratedStyledProperty(DefaultValue = 2.5)]
    public partial double StrokeThickness { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _pen.Brush = BorderBrush;
        _pen.Thickness = StrokeThickness;

        Easing? easing = Easing;
        Size size = Bounds.Size;

        if (easing is null) return;

        for (int i = 0; i < 100; i++)
        {
            double value = Math.Abs(easing.Ease(i / 100f) - 1);
            double after = Math.Abs(easing.Ease((i + 1) / 100f) - 1);

            context.DrawLine(
                _pen,
                new Point(i / 100d * size.Width, value * size.Height),
                new Point((i + 1) / 100d * size.Width, after * size.Height));
        }
    }
}
