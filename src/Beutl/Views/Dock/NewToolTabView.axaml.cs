using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
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
        foreach (ItemsRepeater list in (ItemsRepeater[])[PinnedList, AvailableList, OpenList])
        {
            list.Layout = CreateToolGridLayout();
        }
    }

    // Columns follow the page width: one in a side dock at its usual width, two when a little wider,
    // and at most three, so even a wide dock keeps room between them. Every row is tall enough for
    // two lines, so a wrapped name never changes its height.
    private static UniformGridLayout CreateToolGridLayout()
    {
        return new UniformGridLayout
        {
            Orientation = Orientation.Horizontal,
            ItemsStretch = UniformGridLayoutItemsStretch.Fill,
            MaximumRowsOrColumns = 3,
            MinItemWidth = 168,
            MinItemHeight = 54,
            MinColumnSpacing = 16,
            MinRowSpacing = 2,
        };
    }

    private NewToolTabDockable? NewTab => DataContext as NewToolTabDockable;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.IsAttachedToVisualTree())
        {
            Subscribe();
            NewTab?.Refresh();
            // A recycled page gets the next empty tab without loading again.
            TakeRequestedFocus();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        // A pin made on another page shows here too.
        GlobalConfiguration.Instance.ViewConfig.PinnedToolTabs.CollectionChanged += OnDockablesChanged;
        NewTab?.Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
        GlobalConfiguration.Instance.ViewConfig.PinnedToolTabs.CollectionChanged -= OnDockablesChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        TakeRequestedFocus();
    }

    private void TakeRequestedFocus()
    {
        if (NewTab is { FocusOnShow: true } newTab)
        {
            newTab.FocusOnShow = false;
            SearchBox.Focus();
        }
    }

    // Single-instance tools opened or closed elsewhere, and packages installed or removed while the
    // page is shown, change what it can offer.
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
        factory.ExtensionProvider.ExtensionsChanged += OnExtensionsChanged;
    }

    private void Unsubscribe()
    {
        if (_factory is null) return;

        _factory.DockableAdded -= OnDockablesChanged;
        _factory.DockableRemoved -= OnDockablesChanged;
        _factory.DockableClosed -= OnDockablesChanged;
        _factory.ExtensionProvider.ExtensionsChanged -= OnExtensionsChanged;
        _factory = null;
    }

    // Raised on the thread that installed or removed the package.
    private void OnExtensionsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => OnDockablesChanged(sender, e));
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
                if (NewTab is { } newTab
                    && newTab.PinnedItems.Concat(newTab.AvailableItems).FirstOrDefault() is { } first)
                {
                    e.Handled = true;
                    Open(first);
                }

                break;

            case Key.Down:
                if (NewTab is { } tab
                    && (tab.PinnedItems.Count > 0 ? PinnedList : tab.AvailableItems.Count > 0 ? AvailableList : null) is { } list
                    && FindToolButton(list.GetOrCreateElement(0)) is { } button)
                {
                    e.Handled = true;
                    button.Focus(NavigationMethod.Directional);
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

    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: NewToolTabItem item })
        {
            NewTab?.TogglePin(item.Extension);
        }
    }

    // Each list element is a row panel holding the tool button and its pin button.
    internal static Button? FindToolButton(Control row)
    {
        return (row as Panel)?.Children.OfType<Button>().FirstOrDefault(button => button.Classes.Contains("tool"));
    }

    private void Open(NewToolTabItem item)
    {
        if (NewTab is not { } newTab || newTab.Open(item.Extension)) return;

        // The list may be stale, e.g. a single-instance tool was opened since it was built.
        newTab.Refresh();
        NotificationService.ShowError(Strings.NewTab, MessageStrings.OperationFailed);
    }
}
