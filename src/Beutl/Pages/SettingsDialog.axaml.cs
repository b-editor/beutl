using Avalonia;
using Avalonia.Platform;
using Beutl.Controls.Navigation;
using Beutl.Logging;
using Beutl.Pages.SettingsPages;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.Logging;

namespace Beutl.Pages;

public sealed partial class SettingsDialog : AppWindow
{
    private readonly PageResolver _pageResolver;
    private readonly ILogger _logger = Log.CreateLogger<SettingsDialog>();

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
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        }

        _pageResolver = new PageResolver();
        _ = new NavigationProvider(frame, _pageResolver);

        List<NavigationViewItem> items = GetItems();
        nav.MenuItemsSource = items;
        NavigationViewItem selected = items[0];

        frame.Navigated += Frame_Navigated;
        nav.ItemInvoked += Nav_ItemInvoked;
        nav.BackRequested += Nav_BackRequested;

        nav.SelectedItem = selected;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (nav.SelectedItem is NavigationViewItem selected)
        {
            OnItemInvoked(selected);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
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
        Type pageType = _pageResolver.GetPageType(obj.GetType());

        NavigationTransitionInfo transitionInfo = SharedNavigationTransitionInfo.Instance;
        frame.Navigate(pageType, obj, transitionInfo);
    }

    private static List<NavigationViewItem> GetItems()
    {
        return
        [
            new NavigationViewItem()
            {
                Content = Strings.Account,
                Tag = typeof(AccountSettingsPage),
                IconSource = new SymbolIconSource { Symbol = Symbol.People }
            },
            new NavigationViewItem()
            {
                Content = Strings.View,
                Tag = typeof(ViewSettingsPage),
                IconSource = new SymbolIconSource { Symbol = Symbol.View }
            },
            new NavigationViewItem()
            {
                Content = Strings.Editor,
                Tag = typeof(EditorSettingsPage),
                IconSource = new SymbolIconSource { Symbol = Symbol.Edit }
            },
            new NavigationViewItem()
            {
                Content = Strings.Keymap,
                Tag = typeof(KeyMapSettingsPage),
                IconSource = new SymbolIconSource { Symbol = Symbol.Keyboard }
            },
            new NavigationViewItem()
            {
                Content = Strings.Font,
                Tag = typeof(FontSettingsPage),
                IconSource = new SymbolIconSource { Symbol = Symbol.Font }
            },
            new NavigationViewItem()
            {
                Content = Strings.Extensions,
                Tag = typeof(ExtensionsSettingsPage),
                IconSource = new FluentIcons.FluentAvalonia.SymbolIconSource()
                {
                    Symbol = FluentIcons.Common.Symbol.PuzzlePiece
                }
            },
            new NavigationViewItem()
            {
                Content = Strings.Info,
                Tag = typeof(InformationPage),
                IconSource = new FluentIcons.FluentAvalonia.SymbolIconSource()
                {
                    Symbol = FluentIcons.Common.Symbol.Info
                }
            }
        ];
    }

    private void Nav_BackRequested(object? sender, NavigationViewBackRequestedEventArgs e)
    {
        frame.GoBack();
    }

    private void Nav_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is NavigationViewItem nvi)
        {
            OnItemInvoked(nvi);
        }
    }

    private void OnItemInvoked(NavigationViewItem nvi)
    {
        if (nvi.Tag is Type typ
            && DataContext is SettingsDialogViewModel settingsPage)
        {
            NavigationTransitionInfo transitionInfo = SharedNavigationTransitionInfo.Instance;
            object? parameter = typ.Name switch
            {
                "AccountSettingsPage" => settingsPage.Account,
                "ViewSettingsPage" => settingsPage.View,
                "EditorSettingsPage" => settingsPage.Editor,
                "FontSettingsPage" => settingsPage.Font,
                "ExtensionsSettingsPage" => settingsPage.ExtensionsPage,
                "InformationPage" => settingsPage.Information,
                "KeyMapSettingsPage" => settingsPage.KeyMap,
                _ => null,
            };

            frame.Navigate(typ, parameter, transitionInfo);
        }
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        _logger.LogInformation("Navigate to '{PageName}'.", e.SourcePageType.Name);

        foreach (NavigationViewItem nvi in nav.MenuItemsSource)
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
            if (pagetype == typeof(AccountSettingsPage)
                || pagetype == typeof(ViewSettingsPage)
                || pagetype == typeof(EditorSettingsPage)
                || pagetype == typeof(KeyMapSettingsPage)
                || pagetype == typeof(FontSettingsPage)
                || pagetype == typeof(ExtensionsSettingsPage)
                || pagetype == typeof(InformationPage))
            {
                return 0;
            }
            else if (pagetype == typeof(EditorExtensionPriorityPage)
                     || pagetype == typeof(DecoderPriorityPage)
                     || pagetype == typeof(TelemetrySettingsPage)
                     || pagetype == typeof(AnExtensionSettingsPage))
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public int GetOrder(Type pagetype)
        {
            return pagetype?.Name switch
            {
                "AccountSettingsPage" => 0,
                "ViewSettingsPage" => 1,
                "EditorSettingsPage" => 2,
                "KeyMapSettingsPage" => 3,
                "FontSettingsPage" => 4,
                "ExtensionsSettingsPage" or "EditorExtensionPriorityPage" or "DecoderPriorityPage"
                    or "AnExtensionSettingsPage" => 5,
                "StorageSettingsPage" or "StorageDetailPage" => 6,
                "InformationPage" or "TelemetrySettingsPage" => 7,
                _ => 0,
            };
        }

        public Type GetPageType(Type contextType)
        {
            return contextType?.Name switch
            {
                "AccountSettingsPageViewModel" => typeof(AccountSettingsPage),
                "ViewSettingsPageViewModel" => typeof(ViewSettingsPage),
                "EditorSettingsPageViewModel" => typeof(EditorSettingsPage),
                "KeyMapSettingsPageViewModel" => typeof(KeyMapSettingsPage),
                "FontSettingsPageViewModel" => typeof(FontSettingsPage),
                "ExtensionsSettingsPageViewModel" => typeof(ExtensionsSettingsPage),
                "EditorExtensionPriorityPageViewModel" => typeof(EditorExtensionPriorityPage),
                "DecoderPriorityPageViewModel" => typeof(DecoderPriorityPage),
                "TelemetrySettingsPageViewModel" => typeof(TelemetrySettingsPage),
                "AnExtensionSettingsPageViewModel" => typeof(AnExtensionSettingsPage),
                _ => typeof(InformationPage),
            };
        }
    }
}
