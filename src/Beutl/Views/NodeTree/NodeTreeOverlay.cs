using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Views.NodeTree;

public sealed class NodeTreeOverlay : Control
{
    public static readonly DirectProperty<NodeTreeOverlay, ZoomBorder> ZoomBorderProperty
        = NodeTreeBackground.ZoomBorderProperty.AddOwner<NodeTreeOverlay>(
            o => o.ZoomBorder,
            (o, v) => o.ZoomBorder = v);

    public static readonly DirectProperty<NodeTreeOverlay, Rect> SelectionRangeProperty
        = AvaloniaProperty.RegisterDirect<NodeTreeOverlay, Rect>(
            nameof(SelectionRange), o => o.SelectionRange, (o, v) => o.SelectionRange = v);

    private readonly Pen _pen = new();
    private readonly IBrush _selectionFillBrush = new ImmutableSolidColorBrush(Colors.CornflowerBlue, 0.3);
    private readonly IBrush _selectionStrokeBrush = Brushes.CornflowerBlue;
    private ZoomBorder _zoomBorder = null!;
    private Rect _selectionRange;

    static NodeTreeOverlay()
    {
        AffectsRender<NodeTreeOverlay>(SelectionRangeProperty);
    }

    public ZoomBorder ZoomBorder
    {
        get => _zoomBorder;
        set => SetAndRaise(ZoomBorderProperty, ref _zoomBorder, value);
    }

    public Rect SelectionRange
    {
        get => _selectionRange;
        set => SetAndRaise(SelectionRangeProperty, ref _selectionRange, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZoomBorderProperty)
        {
            if (change.OldValue is ZoomBorder oldValue)
            {
                oldValue.ZoomChanged -= OnZoomChanged;
            }

            if (change.NewValue is ZoomBorder newValue)
            {
                newValue.ZoomChanged += OnZoomChanged;
            }
        }
    }

    private void OnZoomChanged(object sender, ZoomChangedEventArgs e)
    {
        InvalidateVisual();
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
