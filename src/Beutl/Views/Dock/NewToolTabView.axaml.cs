using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Services;
using Beutl.ViewModels.Dock;

namespace Beutl.Views.Dock;

public sealed partial class NewToolTabView : UserControl
{
    private BeutlDockFactory? _factory;
    private bool _refreshQueued;

    public NewToolTabView()
    {
        InitializeComponent();
    }

    private NewToolTabDockable? NewTab => DataContext as NewToolTabDockable;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.IsAttachedToVisualTree())
        {
            Subscribe();
            NewTab?.Refresh();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        NewTab?.Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (NewTab is { FocusOnShow: true } newTab)
        {
            newTab.FocusOnShow = false;
            SearchBox.Focus();
        }
    }

    // Single-instance tools opened or closed elsewhere change what this page can offer.
    private void Subscribe()
    {
        BeutlDockFactory? factory = NewTab?.Factory as BeutlDockFactory;
        if (ReferenceEquals(factory, _factory)) return;

        Unsubscribe();
        _factory = factory;
        if (factory is null) return;

        factory.DockableAdded += OnDockablesChanged;
        factory.DockableRemoved += OnDockablesChanged;
        factory.DockableClosed += OnDockablesChanged;
    }

    private void Unsubscribe()
    {
        if (_factory is null) return;

        _factory.DockableAdded -= OnDockablesChanged;
        _factory.DockableRemoved -= OnDockablesChanged;
        _factory.DockableClosed -= OnDockablesChanged;
        _factory = null;
    }

    private void OnDockablesChanged(object? sender, EventArgs e)
    {
        // Coalesced and deferred: these fire mid-operation (including while this tab is being
        // replaced), and a layout reset raises one per tab.
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (_factory is not null)
                NewTab?.Refresh();
        }, DispatcherPriority.Background);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (NewTab?.AvailableItems.FirstOrDefault() is { } first)
                {
                    e.Handled = true;
                    Open(first);
                }

                break;

            case Key.Down:
                if (NewTab is { AvailableItems.Count: > 0 })
                {
                    e.Handled = true;
                    AvailableList.GetOrCreateElement(0).Focus(NavigationMethod.Directional);
                }

                break;
        }
    }

    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: NewToolTabItem item })
        {
            Open(item);
        }
    }

    private void Open(NewToolTabItem item)
    {
        if (NewTab is not { } newTab || newTab.Open(item.Extension)) return;

        // The list may be stale, e.g. a single-instance tool was opened since it was built.
        newTab.Refresh();
        NotificationService.ShowError(Strings.NewTab, MessageStrings.OperationFailed);
    }
}
