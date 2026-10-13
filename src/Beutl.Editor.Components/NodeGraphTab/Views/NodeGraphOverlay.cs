using Avalonia;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public sealed class NodeGraphOverlay : ZoomBorderBoundControl
{
    public static readonly DirectProperty<NodeGraphOverlay, Rect> SelectionRangeProperty
        = AvaloniaProperty.RegisterDirect<NodeGraphOverlay, Rect>(
            nameof(SelectionRange), o => o.SelectionRange, (o, v) => o.SelectionRange = v);

    private readonly Pen _pen = new();
    private readonly IBrush _selectionFillBrush = new ImmutableSolidColorBrush(Colors.CornflowerBlue, 0.3);
    private readonly IBrush _selectionStrokeBrush = Brushes.CornflowerBlue;
    private Rect _selectionRange;

    static NodeGraphOverlay()
    {
        AffectsRender<NodeGraphOverlay>(SelectionRangeProperty);
    }

    public Rect SelectionRange
    {
        get => _selectionRange;
        set => SetAndRaise(SelectionRangeProperty, ref _selectionRange, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect rect = _selectionRange.Normalize().TransformToAABB(ZoomBorder.Matrix);
        context.FillRectangle(_selectionFillBrush, rect);

        _pen.Thickness = 0.5;
        _pen.Brush = _selectionStrokeBrush;
        context.DrawRectangle(_pen, rect);
    }
}
