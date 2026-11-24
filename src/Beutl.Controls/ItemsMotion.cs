using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Beutl.Controls;

// Reveals a row an ItemsControl gains while it is on screen (an effect added to a list, say). Only a lone addition
// counts: rows filled in when a list opens or is replaced, and rows a reorder removes and inserts again, come with
// other changes and appear at once.
public static partial class ItemsMotion
{
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

    [GeneratedAttachedProperty]
    public static partial bool GetRevealsAddedItems(ItemsControl control);

    private sealed class Tracker : IDisposable
    {
        private readonly ItemsControl _owner;
        private INotifyCollectionChanged? _source;
        // The changes since the last frame, and where the one added row went if that was all of them.
        private int _changes;
        private int _addedIndex = -1;
        private bool _scheduled;

        public Tracker(ItemsControl owner)
        {
            _owner = owner;
            _owner.PropertyChanged += OnOwnerPropertyChanged;
            _owner.AttachedToVisualTree += OnAttached;
            _owner.DetachedFromVisualTree += OnDetached;
            if (TopLevel.GetTopLevel(_owner) is not null) Subscribe();
        }

        public void Dispose()
        {
            Unsubscribe();
            _owner.PropertyChanged -= OnOwnerPropertyChanged;
            _owner.AttachedToVisualTree -= OnAttached;
            _owner.DetachedFromVisualTree -= OnDetached;
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
            _changes = 0;
            _addedIndex = -1;
        }

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess() || TopLevel.GetTopLevel(_owner) is not { } topLevel) return;

            _changes++;
            // A row added while the list is hidden (a list editor fills its rows just before it opens) appears with the
            // list, even if the list shows by the next frame.
            _addedIndex = e is { Action: NotifyCollectionChangedAction.Add, NewItems.Count: 1 } && _owner.IsEffectivelyVisible
                ? e.NewStartingIndex
                : -1;
            // Decided at the start of the next frame, before it draws the new row, once every change made with this
            // one is known.
            if (!_scheduled)
            {
                _scheduled = true;
                topLevel.RequestAnimationFrame(_ => Flush());
            }
        }

        private void Flush()
        {
            int index = _changes == 1 ? _addedIndex : -1;
            _scheduled = false;
            _changes = 0;
            _addedIndex = -1;
            if (index >= 0 && _owner.IsEffectivelyVisible && _owner.ContainerFromIndex(index) is { } container)
                UiMotion.Reveal(container, UiMotion.Normal);
        }
    }
}
