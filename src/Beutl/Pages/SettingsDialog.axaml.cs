using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.Navigation;
using Beutl.Logging;
using Beutl.Pages.SettingsPages;
using Beutl.ViewModels;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.Logging;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;

namespace Beutl.Pages;

public sealed partial class SettingsDialog : FAAppWindow
{
    private readonly PageResolver _pageResolver;
    private readonly ILogger _logger = Log.CreateLogger<SettingsDialog>();
    private object? _pendingNavigation;

    // The open dialog. The macOS application menu stays reachable behind it, so its entries must not open another.
    internal static SettingsDialog? Current { get; private set; }

    public SettingsDialog()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows())
        {
            TitleBar.ExtendsContentIntoTitleBar = true;
            TitleBar.Height = 40;
        }
        else if (OperatingSystem.IsMacOS())
        {
            nav.Margin = new Thickness(0, 22, 0, 0);
            ExtendClientAreaToDecorationsHint = true;
        }

        _pageResolver = new PageResolver();
        _ = new NavigationProvider(frame, _pageResolver);

        List<FANavigationViewItem> items = GetItems();
        nav.MenuItemsSource = items;
        FANavigationViewItem selected = items[0];

        frame.Navigated += Frame_Navigated;
        nav.ItemInvoked += Nav_ItemInvoked;
        nav.BackRequested += Nav_BackRequested;

        nav.SelectedItem = selected;
        frame.Loaded += OnFrameLoaded;
        Closed += OnClosed;
    }

    private void OnFrameLoaded(object? sender, RoutedEventArgs e)
    {
        // Avalonia 12 attaches the window from its base constructor, before InitializeComponent.
        // Navigation also needs the frame's template, so wait for the frame to finish loading.
        if (_pendingNavigation is { } parameter)
        {
            _pendingNavigation = null;
            OnNavigateRequested(parameter);
        }
        else if (frame.Content is null && nav.SelectedItem is FANavigationViewItem selected)
        {
            OnItemInvoked(selected);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Current = this;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (Current == this)
            Current = null;
        frame.Loaded -= OnFrameLoaded;
        _pendingNavigation = null;
        frame.SetNavigationState("|\n0\n0");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsDialogViewModel settingsPage)
        {
            settingsPage.NavigateRequested.Subscribe(OnNavigateRequested);
        }
    }

    private void OnNavigateRequested(object obj)
    {
        // App requests the initial page before calling ShowDialog. Keep the latest request
        // without creating an invisible page or an extra entry in the back stack.
        if (!frame.IsLoaded)
        {
            _pendingNavigation = obj;
            return;
        }

        Type pageType = _pageResolver.GetPageType(obj.GetType());

        FANavigationTransitionInfo transitionInfo = SharedNavigationTransitionInfo.Instance;
        frame.Navigate(pageType, obj, transitionInfo);
    }

    // Each settings page with the type name of its view model and its place in the navigation pane; pages that
    // share an order select the same item. A sub page is opened from another page. The pane's own pages also
    // have the item to show and the view model to open them with.
    private sealed record SettingsPageEntry(
        Type PageType,
        string ContextTypeName,
        int Order,
        bool IsSubPage = false,
        Func<FANavigationViewItem>? CreateItem = null,
        Func<SettingsDialogViewModel, object?>? GetContext = null);

    private static readonly SettingsPageEntry[] s_pages =
    [
        new(typeof(AccountSettingsPage), "AccountSettingsPageViewModel", 0,
            CreateItem: () => NavigationItem(
                SettingsStrings.Account, typeof(AccountSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.People }),
            GetContext: settings => settings.Account),
        new(typeof(ViewSettingsPage), "ViewSettingsPageViewModel", 1,
            CreateItem: () => NavigationItem(
                Strings.View, typeof(ViewSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.Eye }),
            GetContext: settings => settings.View),
        new(typeof(EditorSettingsPage), nameof(EditorSettingsPageViewModel), 2,
            CreateItem: () => NavigationItem(
                Strings.Editor, typeof(EditorSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.Edit }),
            GetContext: settings => settings.Editor),
        new(typeof(PreviewSettingsPage), nameof(PreviewSettingsPageViewModel), 3,
            CreateItem: () => NavigationItem(
                Strings.Preview, typeof(PreviewSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.Video }),
            GetContext: settings => settings.Preview),
        new(typeof(PerformanceSettingsPage), nameof(PerformanceSettingsPageViewModel), 4,
            CreateItem: () => NavigationItem(
                SettingsStrings.Performance, typeof(PerformanceSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.TopSpeed }),
            GetContext: settings => settings.Performance),
        new(typeof(VersionControlSettingsPage), nameof(VersionControlSettingsPageViewModel), 5,
            CreateItem: () => NavigationItem(
                Strings.VersionControl, typeof(VersionControlSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.BranchFork }),
            GetContext: settings => settings.VersionControl),
        new(typeof(KeyMapSettingsPage), "KeyMapSettingsPageViewModel", 6,
            CreateItem: () => NavigationItem(
                SettingsStrings.Keymap, typeof(KeyMapSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.Keyboard }),
            GetContext: settings => settings.KeyMap),
        new(typeof(FontSettingsPage), "FontSettingsPageViewModel", 7,
            CreateItem: () => NavigationItem(
                SettingsStrings.Font, typeof(FontSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.TextFont }),
            GetContext: settings => settings.Font),
        new(typeof(ExtensionsSettingsPage), "ExtensionsSettingsPageViewModel", 8,
            CreateItem: () => NavigationItem(
                Strings.Extensions, typeof(ExtensionsSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.PuzzlePiece }),
            GetContext: settings => settings.ExtensionsPage),
        new(typeof(EditorExtensionPriorityPage), "EditorExtensionPriorityPageViewModel", 8, IsSubPage: true),
        new(typeof(DecoderPriorityPage), "DecoderPriorityPageViewModel", 8, IsSubPage: true),
        new(typeof(AnExtensionSettingsPage), "AnExtensionSettingsPageViewModel", 8, IsSubPage: true),
        new(typeof(AiAgentSettingsPage), "AiAgentSettingsPageViewModel", 9,
            CreateItem: () => NavigationItem(
                SettingsStrings.AiAgents, typeof(AiAgentSettingsPage),
                new FluentIcons.Avalonia.Fluent.SymbolIconSource { Symbol = FluentIcons.Common.Symbol.Chat }),
            GetContext: settings => settings.AiAgent),
        new(typeof(BrowserSettingsPage), "BrowserSettingsPageViewModel", 10,
            CreateItem: () => NavigationItem(
                Strings.WebBrowser, typeof(BrowserSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.Globe }),
            GetContext: settings => settings.Browser),
        new(typeof(TerminalSettingsPage), nameof(TerminalSettingsPageViewModel), 11,
            CreateItem: () => NavigationItem(
                Strings.Terminal, typeof(TerminalSettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.WindowConsole }),
            GetContext: settings => settings.Terminal),
        new(typeof(PrivacySettingsPage), nameof(PrivacySettingsPageViewModel), 12,
            CreateItem: () => NavigationItem(
                SettingsStrings.Privacy, typeof(PrivacySettingsPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.LockClosed }),
            GetContext: settings => settings.Privacy),
        new(typeof(InformationPage), "InformationPageViewModel", 13,
            CreateItem: () => NavigationItem(
                Strings.Info, typeof(InformationPage),
                new FluentIconSource { Icon = FluentIcons.Common.Icon.Info }),
            GetContext: settings => settings.Information),
    ];

    private static FANavigationViewItem NavigationItem(string content, Type pageType, FAIconSource icon)
        => new() { Content = content, Tag = pageType, IconSource = icon };

    private static List<FANavigationViewItem> GetItems()
        => [.. s_pages.Where(page => page.CreateItem is not null).Select(page => page.CreateItem!())];

    private void Nav_BackRequested(object? sender, FANavigationViewBackRequestedEventArgs e)
    {
        frame.GoBack();
    }

    private void Nav_ItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is FANavigationViewItem nvi)
        {
            OnItemInvoked(nvi);
        }
    }

    private void OnItemInvoked(FANavigationViewItem nvi)
    {
        if (nvi.Tag is Type typ
            && DataContext is SettingsDialogViewModel settingsPage)
        {
            FANavigationTransitionInfo transitionInfo = SharedNavigationTransitionInfo.Instance;
            object? parameter = s_pages.FirstOrDefault(page => page.PageType.Name == typ.Name)
                ?.GetContext?.Invoke(settingsPage);

            frame.Navigate(typ, parameter, transitionInfo);
        }
    }

    private void Frame_Navigated(object sender, FANavigationEventArgs e)
    {
        _logger.LogInformation("Navigate to '{PageName}'.", e.SourcePageType.Name);

        foreach (FANavigationViewItem nvi in nav.MenuItemsSource)
        {
            if (nvi.Tag is Type tag)
            {
                int order1 = _pageResolver.GetOrder(tag);
                int order2 = _pageResolver.GetOrder(e.SourcePageType);

                if (order1 == order2)
                {
                    nav.SelectedItem = nvi;
                    break;
                }
            }
        }
    }

    private sealed class PageResolver : IPageResolver
    {
        public int GetDepth(Type pagetype)
        {
            return IsSubPage(pagetype) ? 1 : 0;
        }

        private static bool IsSubPage(Type pagetype)
            => s_pages.Any(page => page.IsSubPage && page.PageType == pagetype);

        // The first navigation has no current page, so pagetype can be null; it counts as order 0.
        public int GetOrder(Type pagetype)
            => s_pages.FirstOrDefault(page => page.PageType.Name == pagetype?.Name)?.Order ?? 0;

        public Type GetPageType(Type contextType)
            => s_pages.FirstOrDefault(page => page.ContextTypeName == contextType?.Name)?.PageType
               ?? typeof(InformationPage);
    }
}
