using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public sealed partial class CloudStorageView : UserControl
{
    // A small, bounded set, independent of the number of remote files.
    public static IReadOnlyList<int> LoadingPlaceholders { get; } = Enumerable.Range(0, 24).ToArray();

    private CompositeDisposable _subscriptions = [];
    private ScrollViewer? _scroll;
    private bool _attached;
    private bool _checkQueued;
    private CancellationTokenSource? _prefetchIntent;

    public CloudStorageView()
    {
        InitializeComponent();
        // Handle activation before the focused item consumes Enter for selection.
        StorageItems.AddHandler(KeyDownEvent, OnItemsKeyDown, RoutingStrategies.Tunnel);
        StorageItems.TemplateApplied += (_, e) =>
        {
            SetScrollViewer(e.NameScope.Find<ScrollViewer>("PART_ScrollViewer"));
            QueueLoadMore();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        SetScrollViewer(StorageItems.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault());
        SubscribeToListing();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        CancelPrefetchIntent();
        _subscriptions.Dispose();
        SetScrollViewer(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        CancelPrefetchIntent();
        if (_attached) SubscribeToListing();
    }

    private void SubscribeToListing()
    {
        _subscriptions.Dispose();
        _subscriptions = [];
        if (DataContext is not CloudStorageViewModel vm) return;
        vm.Items.CollectionChanged += OnItemsChanged;
        Disposable.Create(() => vm.Items.CollectionChanged -= OnItemsChanged).DisposeWith(_subscriptions);
        vm.IsLoading.Subscribe(_ => QueueLoadMore()).DisposeWith(_subscriptions);
        vm.IsLoadingMore.Subscribe(_ => QueueLoadMore()).DisposeWith(_subscriptions);
        vm.ViewMode.Subscribe(_ => QueueLoadMore()).DisposeWith(_subscriptions);
        QueueLoadMore();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            CancelPrefetchIntent();
            StorageItems.SelectedItem = null;
            _scroll?.ScrollToHome();
        }
        QueueLoadMore();
    }

    private void SetScrollViewer(ScrollViewer? scroll)
    {
        if (_scroll != null) _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = scroll;
        if (_scroll != null) _scroll.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => QueueLoadMore();

    private void QueueLoadMore()
    {
        if (!_attached || _checkQueued) return;
        _checkQueued = true;
        Dispatcher.UIThread.Post(async () =>
        {
            _checkQueued = false;
            if (!_attached || !IsEffectivelyVisible || DataContext is not CloudStorageViewModel vm) return;
            // Appending and changing display modes invalidate the extent. Measure first so a
            // stale extent cannot cause every remaining page to be fetched in a tight loop.
            StorageItems.UpdateLayout();
            if (_scroll is not { Viewport.Height: > 0 } scroll
                || vm.IsLoading.Value || vm.IsLoadingMore.Value || vm.LoadMoreError.Value != null
                || vm.Error.Value != null || !vm.HasMore.Value) return;
            double remaining = scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y;
            // Keep roughly one viewport ready ahead of the user. Stop once that buffer is filled;
            // opening the view must not eagerly download the entire listing.
            if (remaining > Math.Max(1, scroll.Viewport.Height)) return;
            await vm.LoadMoreAsync();
            QueueLoadMore();
        }, DispatcherPriority.Background);
    }

    private async void OnAccountSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CloudStorageViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            await vm.OpenAccountSettingsAsync(owner);
    }

    private void CancelPrefetchIntent()
    {
        _prefetchIntent?.Cancel();
        _prefetchIntent?.Dispose();
        _prefetchIntent = null;
    }

    private async void QueueFolderPrefetch(CloudStorageItem? item)
    {
        CancelPrefetchIntent();
        if (!_attached || item is not { IsFolder: true } || DataContext is not CloudStorageViewModel vm) return;
        var intent = new CancellationTokenSource();
        _prefetchIntent = intent;
        try
        {
            await Task.Delay(120, intent.Token);
            if (_attached && ReferenceEquals(_prefetchIntent, intent) && ReferenceEquals(DataContext, vm))
                await vm.PrefetchFolderAsync(item);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_prefetchIntent, intent))
            {
                _prefetchIntent = null;
                intent.Dispose();
            }
        }
    }

    private void OnItemPointerEntered(object? sender, PointerEventArgs e) =>
        QueueFolderPrefetch((sender as Control)?.DataContext as CloudStorageItem);

    private void OnItemPointerExited(object? sender, PointerEventArgs e) => CancelPrefetchIntent();

    private void OnItemFocused(object? sender, FocusChangedEventArgs e) =>
        QueueFolderPrefetch((e.Source as Control)?.DataContext as CloudStorageItem);

    private void OnItemLostFocus(object? sender, RoutedEventArgs e) => CancelPrefetchIntent();

    private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CloudStorageViewModel vm
            && e.Source is Visual source
            && source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is CloudStorageItem item)
            await vm.OpenFolderAsync(item);
    }

    private async void OnItemsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is CloudStorageViewModel vm
            && sender is ListBox { SelectedItem: CloudStorageItem { IsFolder: true } item })
        {
            e.Handled = true;
            await vm.OpenFolderAsync(item);
        }
    }
}
