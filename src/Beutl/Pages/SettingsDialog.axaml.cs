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

public sealed partial class SettingsDialog : FAAppWindow
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

        List<FANavigationViewItem> items = GetItems();
        nav.MenuItemsSource = items;
        FANavigationViewItem selected = items[0];

        frame.Navigated += Frame_Navigated;
        nav.ItemInvoked += Nav_ItemInvoked;
        nav.BackRequested += Nav_BackRequested;

        nav.SelectedItem = selected;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (nav.SelectedItem is FANavigationViewItem selected)
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

        FANavigationTransitionInfo transitionInfo = SharedNavigationTransitionInfo.Instance;
        frame.Navigate(pageType, obj, transitionInfo);
    }

    private static List<FANavigationViewItem> GetItems()
    {
        return
        [
            new FANavigationViewItem()
            {
                Content = SettingsStrings.Account,
                Tag = typeof(AccountSettingsPage),
                IconSource = new FASymbolIconSource { Symbol = FASymbol.People }
            },
            new FANavigationViewItem()
            {
                Content = Strings.View,
                Tag = typeof(ViewSettingsPage),
                IconSource = new FASymbolIconSource { Symbol = FASymbol.View }
            },
            new FANavigationViewItem()
            {
                Content = Strings.Editor,
                Tag = typeof(EditorSettingsPage),
                IconSource = new FASymbolIconSource { Symbol = FASymbol.Edit }
            },
            new FANavigationViewItem()
            {
                Content = SettingsStrings.Keymap,
                Tag = typeof(KeyMapSettingsPage),
                IconSource = new FASymbolIconSource { Symbol = FASymbol.Keyboard }
            },
            new FANavigationViewItem()
            {
                Content = SettingsStrings.Font,
                Tag = typeof(FontSettingsPage),
                IconSource = new FASymbolIconSource { Symbol = FASymbol.Font }
            },
            new FANavigationViewItem()
            {
                Content = Strings.Extensions,
                Tag = typeof(ExtensionsSettingsPage),
                IconSource = new FluentIcons.FluentAvalonia.SymbolIconSource()
                {
                    Symbol = FluentIcons.Common.Symbol.PuzzlePiece
                }
            },
            new FANavigationViewItem()
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
