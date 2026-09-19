using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Beutl.Views.Tools;

/// <summary>
/// Fixed-size storage tiles with pixel scrolling. Only viewport rows, one buffer row on each
/// side, and the keyboard's active container are retained as the listing grows.
/// </summary>
public sealed class StorageIconPanel : VirtualizingPanel, ILogicalScrollable
{
    internal const double TileWidth = 92;
    internal const double TileHeight = 128;
    private readonly Dictionary<int, Control> _realized = [];
    private Vector _offset;
    private int _columns = 1;

    public StorageIconPanel() => ClipToBounds = true;

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public bool IsLogicalScrollEnabled => true;
    public Size ScrollSize => new(TileWidth, TileHeight / 3);
    public Size PageScrollSize => new(0, Math.Max(TileHeight, Viewport.Height - TileHeight));
    public Size Extent { get; private set; }
    public Size Viewport { get; private set; }
    public event EventHandler? ScrollInvalidated;

    public Vector Offset
    {
        get => _offset;
        set
        {
            var offset = new Vector(0, Math.Clamp(value.Y, 0, Math.Max(0, Extent.Height - Viewport.Height)));
            if (_offset == offset) return;
            _offset = offset;
            InvalidateMeasure();
            RaiseScrollInvalidated(EventArgs.Empty);
        }
    }

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : TileWidth;
        _columns = Math.Max(1, (int)(width / TileWidth));
        double height = Math.Ceiling((double)Items.Count / _columns) * TileHeight;
        var extent = new Size(width, height);
        // The first measure can precede the ScrollViewer's logical-scroll subscription. Request
        // another measure with a bounded viewport instead of briefly realizing the entire list.
        var viewport = new Size(width, double.IsFinite(availableSize.Height) ? availableSize.Height : TileHeight);
        bool changed = Extent != extent || Viewport != viewport;
        Extent = extent;
        Viewport = viewport;
        Offset = _offset;
        RealizeViewport();
        foreach (var container in _realized.Values)
            container.Measure(new Size(TileWidth, TileHeight));
        if (changed) RaiseScrollInvalidated(EventArgs.Empty);
        return new Size(width, Math.Min(height, viewport.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, container) in _realized)
            container.Arrange(new Rect(index % _columns * TileWidth,
                index / _columns * TileHeight - Offset.Y, TileWidth, TileHeight));
        return finalSize;
    }

    private void RealizeViewport()
    {
        int first = Math.Max(0, (int)(Offset.Y / TileHeight) - 1) * _columns;
        int end = Math.Min(Items.Count, ((int)Math.Ceiling((Offset.Y + Viewport.Height) / TileHeight) + 1) * _columns);
        foreach (var (index, container) in _realized.ToArray())
        {
            if (index >= first && index < end) continue;
            // Keep focus/Tab navigation intact when the user scrolls the selected tile off screen.
            if (container.IsKeyboardFocusWithin || KeyboardNavigation.GetTabOnceActiveElement(ItemsControl!) == container)
                continue;
            RemoveContainer(index, container);
        }
        for (int i = first; i < end; i++) GetOrCreateContainer(i);
    }

    private Control GetOrCreateContainer(int index)
    {
        if (_realized.TryGetValue(index, out var container)) return container;
        var generator = ItemContainerGenerator!;
        var item = Items[index];
        // This panel belongs to the storage ListBox, whose items are data records, never controls.
        generator.NeedsContainer(item, index, out var recycleKey);
        container = generator.CreateContainer(item, index, recycleKey);
        _realized.Add(index, container);
        generator.PrepareItemContainer(container, item, index);
        AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);
        return container;
    }

    private void RemoveContainer(int index, Control container)
    {
        _realized.Remove(index);
        ItemContainerGenerator!.ClearItemContainer(container);
        RemoveInternalChild(container);
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        // Appends leave every existing index and scroll position intact. Other mutations (folder
        // navigation, refresh, account changes) must release containers for the old listing.
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex != items.Count - e.NewItems!.Count)
        {
            foreach (var (index, container) in _realized.ToArray()) RemoveContainer(index, container);
        }
        if (e.Action == NotifyCollectionChangedAction.Reset) Offset = default;
        InvalidateMeasure();
    }

    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        var containers = _realized.Values.ToArray();
        _realized.Clear();
        foreach (var container in containers) oldValue?.ItemContainerGenerator.ClearItemContainer(container);
        base.OnItemsControlChanged(oldValue);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Release the owner's logical children while it is still available during panel detach.
        foreach (var (index, container) in _realized.ToArray()) RemoveContainer(index, container);
        InvalidateMeasure();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Control? ContainerFromIndex(int index) => _realized.GetValueOrDefault(index);

    protected override int IndexFromContainer(Control container)
    {
        foreach (var (index, candidate) in _realized)
            if (ReferenceEquals(candidate, container)) return index;
        return -1;
    }

    protected override IEnumerable<Control> GetRealizedContainers() => _realized.OrderBy(x => x.Key).Select(x => x.Value);

    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count) return null;
        double top = index / _columns * TileHeight;
        if (top < Offset.Y) Offset = new Vector(0, top);
        else if (top + TileHeight > Offset.Y + Viewport.Height)
            Offset = new Vector(0, top + TileHeight - Viewport.Height);
        var container = GetOrCreateContainer(index);
        InvalidateMeasure();
        return container;
    }

    public bool BringIntoView(Control target, Rect targetRect)
    {
        var container = target.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(x => x.GetVisualParent() == this);
        int index = container != null ? IndexFromContainer(container) : -1;
        var previous = Offset;
        ScrollIntoView(index);
        return previous != Offset;
    }

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) =>
        GetControl(direction, from, false) as Control;

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        int index = from is Control container ? IndexFromContainer(container) : -1;
        int next = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => Items.Count - 1,
            NavigationDirection.Next or NavigationDirection.Right => index + 1,
            NavigationDirection.Previous or NavigationDirection.Left => index - 1,
            NavigationDirection.Down => index + _columns,
            NavigationDirection.Up => index - _columns,
            NavigationDirection.PageDown => Math.Min(Items.Count - 1,
                index + Math.Max(1, (int)(Viewport.Height / TileHeight)) * _columns),
            NavigationDirection.PageUp => Math.Max(0,
                index - Math.Max(1, (int)(Viewport.Height / TileHeight)) * _columns),
            _ => -1,
        };
        if (wrap && Items.Count > 0) next = (next % Items.Count + Items.Count) % Items.Count;
        return ScrollIntoView(next);
    }
}
