#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Beutl.Controls;

public class TreeLineDecorator : Decorator
{
    public static readonly StyledProperty<int> IndentLevelProperty =
        AvaloniaProperty.Register<TreeLineDecorator, int>(nameof(IndentLevel), 1);

    public static readonly StyledProperty<double> IndentWidthProperty =
        AvaloniaProperty.Register<TreeLineDecorator, double>(nameof(IndentWidth), 16.0);

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<TreeLineDecorator, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<TreeLineDecorator, double>(nameof(LineThickness), 1.0);

    static TreeLineDecorator()
    {
        AffectsRender<TreeLineDecorator>(IndentLevelProperty, IndentWidthProperty, LineBrushProperty, LineThicknessProperty, UseLayoutRoundingProperty);
        AffectsMeasure<TreeLineDecorator>(IndentLevelProperty, IndentWidthProperty);
        AffectsArrange<TreeLineDecorator>(IndentLevelProperty, IndentWidthProperty);
    }

    public int IndentLevel
    {
        get => GetValue(IndentLevelProperty);
        set => SetValue(IndentLevelProperty, value);
    }

    public double IndentWidth
    {
        get => GetValue(IndentWidthProperty);
        set => SetValue(IndentWidthProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (IndentLevel <= 0 || LineBrush == null)
            return;

        double height = Bounds.Height;
        double scale = LayoutHelper.GetLayoutScale(this);
        double thickness = UseLayoutRounding
            ? LayoutHelper.RoundLayoutValueUp(LineThickness, scale)
            : LineThickness;

        // Draw vertical lines for each indent level
        for (int i = 0; i < IndentLevel; i++)
        {
            double x = (i * IndentWidth) + (IndentWidth / 2.0) - (thickness / 2.0);
            // Like Separator, fill whole device pixels instead of spreading a
            // one-pixel line across two columns at the indent's half-pixel edge.
            if (UseLayoutRounding)
                x = LayoutHelper.RoundLayoutValue(x, scale);
            var rect = new Rect(x, 0, thickness, height);
            context.FillRectangle(LineBrush, rect);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double leftMargin = IndentLevel * IndentWidth;

        if (Child == null) return new Size(leftMargin, 0);

        Child.Measure(new Size(Math.Max(0, availableSize.Width - leftMargin), availableSize.Height));
        return new Size(Child.DesiredSize.Width + leftMargin, Child.DesiredSize.Height);

    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double leftMargin = IndentLevel * IndentWidth;

        if (Child == null) return finalSize;

        var childRect = new Rect(leftMargin, 0, Math.Max(0, finalSize.Width - leftMargin), finalSize.Height);
        Child.Arrange(childRect);

        return finalSize;
    }
}
