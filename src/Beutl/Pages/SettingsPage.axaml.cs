using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

using Beutl.Controls;
using Beutl.Controls.Navigation;
using Beutl.Pages.SettingsPages;
using Beutl.ViewModels;
using Beutl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages;

public sealed partial class SettingsPage : UserControl
{
    private readonly PageResolver _pageResolver;

    public SettingsPage()
    {
        InitializeComponent();
        _pageResolver = new PageResolver();
        _ = new NavigationProvider(frame, _pageResolver);

        List<NavigationViewItem> items = GetItems();
        nav.MenuItems = items;
        NavigationViewItem selected = items[0];

        frame.Navigated += Frame_Navigated;
        nav.ItemInvoked += Nav_ItemInvoked;
        nav.BackRequested += Nav_BackRequested;

        nav.SelectedItem = selected;

        this.GetObservable(IsVisibleProperty).Subscribe(b =>
        {
            if (b)
            {
                if (nav.SelectedItem is NavigationViewItem selected)
                {
                    OnItemInvoked(selected);
                }
            }
            else
            {
                frame.SetNavigationState("|\n0\n0");
            }
        });
    }

    //protected override async void OnDataContextChanged(EventArgs e)
    //{
    //    base.OnDataContextChanged(e);
    //    if (DataContext is SettingsPageViewModel settingsPage)
    //    {
    //        await _navigationProvider.NavigateAsync(_ => true, () => settingsPage.Account);
    //    }
    //}

    private static List<NavigationViewItem> GetItems()
    {
        return new List<NavigationViewItem>()
        {
            new NavigationViewItem()
            {
                Content = Strings.Account,
                Tag = typeof(AccountSettingsPage),
                IconSource = new SymbolIconSource
                {
                    Symbol = Symbol.People
                }
            },
            new NavigationViewItem()
            {
                Content = Strings.View,
                Tag = typeof(ViewSettingsPage),
                IconSource = new SymbolIconSource
                {
                    Symbol = Symbol.View
                }
            },
            new NavigationViewItem()
            {
                Content = Strings.Font,
                Tag = typeof(FontSettingsPage),
                IconSource = new SymbolIconSource
                {
                    Symbol = Symbol.Font
                }
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
                Content = Language.SettingsPage.Storage,
                Tag = typeof(StorageSettingsPage),
                IconSource = new FluentIcons.FluentAvalonia.SymbolIconSource()
                {
                    Symbol = FluentIcons.Common.Symbol.Storage
                }
            },
            new NavigationViewItem()
            {
                Content = Strings.Info,
                Tag = typeof(InfomationPage),
                IconSource = new FluentIcons.FluentAvalonia.SymbolIconSource()
                {
                    Symbol = FluentIcons.Common.Symbol.Info
                }
            }
        };
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
            && DataContext is SettingsPageViewModel settingsPage)
        {
            NavigationTransitionInfo transitionInfo = SharedNavigationTransitionInfo.Instance;
            object? parameter = null;
            if (typ == typeof(AccountSettingsPage))
            {
                parameter = settingsPage.Account;
            }
            else if (typ == typeof(StorageSettingsPage))
            {
                parameter = settingsPage.Storage;
            }

            frame.Navigate(typ, parameter, transitionInfo);
        }
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        foreach (NavigationViewItem nvi in nav.MenuItems)
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
                || pagetype == typeof(FontSettingsPage)
                || pagetype == typeof(ExtensionsSettingsPage)
                || pagetype == typeof(StorageSettingsPage)
                || pagetype == typeof(InfomationPage))
            {
                return 0;
            }
            else if (pagetype == typeof(StorageDetailPage))
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
                "FontSettingsPage" => 2,
                "ExtensionsSettingsPage" => 3,
                "StorageSettingsPage" or "StorageDetailPage" => 4,
                "InfomationPage" => 5,
                _ => 0,
            };
        }

        public Type GetPageType(Type contextType)
        {
            return contextType?.Name switch
            {
                "AccountSettingsPageViewModel" => typeof(AccountSettingsPage),
                "ViewSettingsPageViewModel" => typeof(ViewSettingsPage),
                "FontSettingsPageViewModel" => typeof(FontSettingsPage),
                "ExtensionsSettingsPageViewModel" => typeof(ExtensionsSettingsPage),
                "StorageSettingsPageViewModel" => typeof(StorageSettingsPage),
                "StorageDetailPageViewModel" => typeof(StorageDetailPage),
                _ => typeof(InfomationPage),
            };
        }
    }
}
