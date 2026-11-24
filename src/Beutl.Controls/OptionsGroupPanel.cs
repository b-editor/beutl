using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Controls;

/// <summary>
/// Stacks settings rows and separates visible rows, including generated item containers.
/// </summary>
public sealed partial class OptionsGroupPanel : StackPanel
{
    private double[] _separatorPositions = [];
    private readonly SeparatorLayer _separatorLayer;

    public OptionsGroupPanel()
    {
        Spacing = 1;
        // Keep this visual after the item visuals and outside Children so it does not
        // participate in item generation, indexing, keyboard navigation or measurement.
        _separatorLayer = new SeparatorLayer(this) { IsHitTestVisible = false };
        VisualChildren.Add(_separatorLayer);
    }

    [GeneratedAttachedProperty(Inherits = true)]
    public static partial bool GetIsGrouped(Control control);

    [GeneratedStyledProperty]
    public partial IBrush? SeparatorBrush { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeparatorBrushProperty)
            _separatorLayer.InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        List<double> positions = [];
        Control? previous = null;
        foreach (Control child in Children)
        {
            if (!child.IsVisible || child.Bounds.Height <= 0)
                continue;

            if (previous is not null)
                positions.Add((previous.Bounds.Bottom + child.Bounds.Top) / 2);
            previous = child;
        }

        if (!_separatorPositions.SequenceEqual(positions))
        {
            _separatorPositions = positions.ToArray();
            _separatorLayer.InvalidateVisual();
        }
        _separatorLayer.Measure(finalSize);
        _separatorLayer.Arrange(new Rect(finalSize));
        return result;
    }

    private sealed class SeparatorLayer(OptionsGroupPanel owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (owner.SeparatorBrush is null || Bounds.Width <= 24)
                return;

            var pen = new Pen(owner.SeparatorBrush);
            foreach (double y in owner._separatorPositions)
                context.DrawLine(pen, new Point(12, y), new Point(Bounds.Width - 12, y));
        }
    }
}
