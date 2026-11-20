using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Beutl.Controls;

// Reveals the rows an ItemsControl gains while it is on screen (an effect added to a list, say). Rows it shows when it
// opens or when its items are replaced appear at once, and rows that only move keep their place.
public static class ItemsMotion
{
    public static readonly AttachedProperty<bool> RevealsAddedItemsProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("RevealsAddedItems", typeof(ItemsMotion));

    private static readonly AttachedProperty<Tracker?> TrackerProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, Tracker?>("Tracker", typeof(ItemsMotion));

    static ItemsMotion()
    {
        RevealsAddedItemsProperty.Changed.AddClassHandler<ItemsControl>((itemsControl, e) =>
        {
            itemsControl.GetValue(TrackerProperty)?.Dispose();
            itemsControl.SetValue(TrackerProperty, e.GetNewValue<bool>() ? new Tracker(itemsControl) : null);
        });
    }

    public static bool GetRevealsAddedItems(ItemsControl control) => control.GetValue(RevealsAddedItemsProperty);

    public static void SetRevealsAddedItems(ItemsControl control, bool value) =>
        control.SetValue(RevealsAddedItemsProperty, value);

    private sealed class Tracker : IDisposable
    {
        private readonly ItemsControl _owner;
        // Items added before their row exists, revealed when it is prepared.
        private readonly List<object> _pending = [];
        private INotifyCollectionChanged? _source;

        public Tracker(ItemsControl owner)
        {
            _owner = owner;
            _owner.PropertyChanged += OnOwnerPropertyChanged;
            _owner.AttachedToVisualTree += OnAttached;
            _owner.DetachedFromVisualTree += OnDetached;
            _owner.ContainerPrepared += OnContainerPrepared;
            if (TopLevel.GetTopLevel(_owner) is not null) Subscribe();
        }

        public void Dispose()
        {
            Unsubscribe();
            _owner.PropertyChanged -= OnOwnerPropertyChanged;
            _owner.AttachedToVisualTree -= OnAttached;
            _owner.DetachedFromVisualTree -= OnDetached;
            _owner.ContainerPrepared -= OnContainerPrepared;
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Subscribe();

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Unsubscribe();

        private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ItemsControl.ItemsSourceProperty && TopLevel.GetTopLevel(_owner) is not null)
                Subscribe();
        }

        // The source itself rather than the ItemsControl's view of it, which also reports the rows of a new source as
        // added. Only while the control is in a window, so a list that outlives it does not keep it.
        private void Subscribe()
        {
            Unsubscribe();
            _source = _owner.ItemsSource as INotifyCollectionChanged;
            if (_source is not null) _source.CollectionChanged += OnSourceChanged;
        }

        private void Unsubscribe()
        {
            if (_source is not null) _source.CollectionChanged -= OnSourceChanged;
            _source = null;
            _pending.Clear();
        }

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add
                || e.NewItems is not IList items
                || !Dispatcher.UIThread.CheckAccess()
                || !_owner.IsEffectivelyVisible
                || TopLevel.GetTopLevel(_owner) is not { } topLevel)
            {
                return;
            }

            foreach (object? item in items)
            {
                if (item is null) continue;
                if (_owner.ContainerFromItem(item) is { } container)
                    UiMotion.Reveal(container, UiMotion.Normal);
                else
                    _pending.Add(item);
            }

            // A row not prepared by the next frame is not shown, so it is not revealed later either.
            if (_pending.Count > 0)
                topLevel.RequestAnimationFrame(_ => _pending.Clear());
        }

        private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (_pending.Count == 0 || _owner.ItemFromContainer(e.Container) is not { } item) return;
            int index = _pending.FindIndex(pending => ReferenceEquals(pending, item));
            if (index < 0) return;
            _pending.RemoveAt(index);
            UiMotion.Reveal(e.Container, UiMotion.Normal);
        }
    }
}
