using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Pages.SettingsPages;
using BeUtl.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();

        nav.MenuItems = GetItems();

        frame.Navigated += Frame_Navigated;
        frame.Navigating += Frame_Navigating;
        nav.ItemInvoked += Nav_ItemInvoked;
        nav.BackRequested += Nav_BackRequested;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var items = (List<NavigationViewItem>)nav.MenuItems;

        NavigationViewItem selected = items[0];

        nav.SelectedItem = selected;
        frame.Navigate((Type)selected.Tag!);
    }

    private static List<NavigationViewItem> GetItems()
    {
        return new List<NavigationViewItem>()
        {
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.AccountObservable.ToBinding(),
                Tag = typeof(AccountSettingsPage),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.People
                }
            },
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.ViewObservable.ToBinding(),
                Tag = typeof(ViewSettingsPage),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.View
                }
            },
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.FontObservable.ToBinding(),
                Tag = typeof(FontSettingsPage),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Font
                }
            },
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.ExtensionsObservable.ToBinding(),
                Tag = typeof(ExtensionsSettingsPage),
                Icon = new FluentIcons.FluentAvalonia.SymbolIcon()
                {
                    Symbol = FluentIcons.Common.Symbol.PuzzlePiece
                }
            },
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.StorageObservable.ToBinding(),
                Tag = typeof(StorageSettingsPage),
                Icon = new FluentIcons.FluentAvalonia.SymbolIcon()
                {
                    Symbol = FluentIcons.Common.Symbol.Storage
                }
            },
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.InfoObservable.ToBinding(),
                Tag = typeof(InfomationPage),
                Icon = new FluentIcons.FluentAvalonia.SymbolIcon()
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
        if (e.InvokedItemContainer is NavigationViewItem nvi && nvi.Tag is Type typ)
        {
            frame.Navigate(typ, null, e.RecommendedNavigationTransitionInfo);
        }
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is Control control)
        {
            if (e.Parameter is { })
            {
                control.DataContext = e.Parameter;
            }
            else if (DataContext is SettingsPageViewModel settingsPage)
            {
                control.DataContext = control switch
                {
                    AccountSettingsPage => settingsPage.Account,
                    StorageSettingsPage => settingsPage.Storage,
                    _ => control.DataContext
                };
            }

            control.Focus();
        }

        foreach (NavigationViewItem nvi in nav.MenuItems)
        {
            if (nvi.Tag is Type tag)
            {
                (int Order, int Depth) navItemNum = ToNumber(tag);
                (int Order, int Depth) pageNum = ToNumber(e.SourcePageType);

                if (navItemNum.Order == pageNum.Order)
                {
                    nav.SelectedItem = nvi;
                    break;
                }
            }
        }
    }

    private void Frame_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.NavigationTransitionInfo is EntranceNavigationTransitionInfo entrance)
        {
            Type type1 = frame.CurrentSourcePageType;
            Type type2 = e.SourcePageType;
            (int Order, int Depth) num1 = ToNumber(type1);
            (int Order, int Depth) num2 = ToNumber(type2);
            double horizontal = 28;
            double vertical = 28;

            if (num1.Order == num2.Order)
            {
                horizontal *= Math.Clamp(num2.Depth - num1.Depth, -1, 1);
                vertical = 0;
            }
            else if (num1.Order != num2.Order)
            {
                horizontal = 0;
                vertical *= Math.Clamp(num2.Order - num1.Order, -1, 1);
            }

            entrance.FromHorizontalOffset = horizontal;
            entrance.FromVerticalOffset = vertical;
        }
    }

    private static (int Order, int Depth) ToNumber(Type type)
    {
        if (type == typeof(AccountSettingsPage))
            return (0, 0);
        else if (type == typeof(ViewSettingsPage))
            return (1, 0);
        else if (type == typeof(FontSettingsPage))
            return (2, 0);
        else if (type == typeof(ExtensionsSettingsPage))
            return (3, 0);
        else if (type == typeof(StorageSettingsPage))
            return (4, 0);
        else if (type == typeof(InfomationPage))
            return (5, 0);

        else if (type == typeof(StorageDetailPage))
            return (4, 1);

        return (0, 0);
    }
}
