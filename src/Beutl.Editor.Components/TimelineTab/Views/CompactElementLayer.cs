using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Beutl.Editor.Components.TimelineTab.Views;

// A narrow clip as a surface paints and hits it, with or without a view model.
internal interface ICompactClip
{
    Rect Bounds { get; }

    bool IsSelected { get; }

    double Opacity { get; }

    IBrush FillBrush { get; }

    IPen RestBorderPen { get; }
}

// One drawing surface for a run of narrow clips. Selected clips use a separate surface at the same
// ZIndex as selected ElementViews, preserving paint and hit-test order when detail levels are mixed.
internal sealed class CompactElementLayer : Control, ICustomHitTest
{
    private readonly bool _selected;
    private readonly Func<ICompactClip, ElementView?> _realize;
    // Filled again on every pass, which runs for every frame of a scroll, so the list is kept rather than replaced.
    private readonly List<ICompactClip> _elements = [];
    private Vector _offset;
    private Pen? _selectionPen;

    public CompactElementLayer(bool selected, Func<ICompactClip, ElementView?> realize)
    {
        _selected = selected;
        _realize = realize;
        ZIndex = selected ? 5 : 0;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        ClipToBounds = true;
        UseLayoutRounding = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    internal int ElementCount => _elements.Count(entry => entry.IsSelected == _selected);

    public void Update(IReadOnlyList<ICompactClip> elements, Rect viewport)
    {
        _elements.Clear();
        _elements.AddRange(elements);
        _offset = new Vector(viewport.X, viewport.Y);
        Margin = new Thickness(viewport.X, viewport.Y, 0, 0);
        Width = viewport.Width;
        Height = viewport.Height;
        InvalidateVisual();
    }

    // The opacity of a clip that cannot be edited or is disabled, as ElementView's elm-locked and elm-disabled styles
    // give it.
    internal static double ClipOpacity(bool editable, bool enabled) => !editable ? 0.6 : !enabled ? 0.5 : 1;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IPen? selectionPen = _selected ? GetSelectionPen() : null;
        foreach (ICompactClip entry in _elements)
        {
            if (entry.IsSelected != _selected) continue;
            Rect bounds = entry.Bounds.Translate(-_offset);
            if (bounds.Width <= 0 || bounds.Height <= 2 || !bounds.Intersects(new Rect(Bounds.Size))) continue;
            double opacity = entry.Opacity;
            using var state = opacity == 1 ? default : context.PushOpacity(opacity);
            IPen pen = selectionPen ?? entry.RestBorderPen;
            if (bounds.Width <= 2)
                context.FillRectangle(pen.Brush!, new Rect(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height - 2));
            else
                context.DrawRectangle(entry.FillBrush, pen,
                    new Rect(bounds.X + 0.5, bounds.Y + 1.5, bounds.Width - 1, bounds.Height - 3), 3, 3);
        }
    }

    // The brush is looked up on every paint, as a theme can replace it, and the pen is made again only then.
    private Pen GetSelectionPen()
    {
        IBrush brush = this.TryFindResource("TextControlForeground", out object? resource)
            && resource is IBrush found ? found : Brushes.White;
        if (_selectionPen is null || !ReferenceEquals(_selectionPen.Brush, brush))
            _selectionPen = new Pen(brush);
        return _selectionPen;
    }

    private ICompactClip? FindElement(Point localPoint)
    {
        Point point = localPoint + _offset;
        for (int i = _elements.Count - 1; i >= 0; i--)
        {
            ICompactClip entry = _elements[i];
            if (entry.IsSelected == _selected && entry.Bounds.Contains(point)) return entry;
        }
        return null;
    }

    // CompositionDrawListVisual passes a point transformed into this control's coordinates.
    public bool HitTest(Point point) => FindElement(point) is not null;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (TopLevel.GetTopLevel(this) is { } root && FindElement(e.GetPosition(this)) is { } entry
            && _realize(entry) is { } view)
        {
            ForwardMove(view, root, e);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TopLevel.GetTopLevel(this) is not { } root || FindElement(e.GetPosition(this)) is not { } entry) return;
        Point position = e.GetPosition(root);
        PointerPointProperties properties = e.GetCurrentPoint(root).Properties;
        bool transferCapture = ReferenceEquals(e.Pointer.Captured, this);
        if (_realize(entry) is not { } view) return;

        // The mouse device captured this surface before routing the press. The normal move behavior
        // starts its drag on the next move, so that implicit capture must follow the promoted border.
        if (transferCapture) e.Pointer.Capture(view.border);

        // Touch and clicks without a preceding move still need the normal resize/slip cursor state.
        ForwardMove(view, root, e);
        view.border.RaiseEvent(new PointerPressedEventArgs(view.border, e.Pointer, root, position,
            e.Timestamp, properties, e.KeyModifiers, e.ClickCount));
        e.Handled = true;
    }

    private static void ForwardMove(ElementView view, Visual root, PointerEventArgs e)
        => view.border.RaiseEvent(new PointerEventArgs(PointerMovedEvent, view.border, e.Pointer, root,
            e.GetPosition(root), e.Timestamp, e.GetCurrentPoint(root).Properties, e.KeyModifiers));
}
