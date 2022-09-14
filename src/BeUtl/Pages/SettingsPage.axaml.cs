using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Pages.SettingsPages;
using BeUtl.ViewModels;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Pages;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();

        List<NavigationViewItem> items = GetItems();
        nav.MenuItems = items;
        NavigationViewItem selected = items[0];

        frame.Navigated += Frame_Navigated;
        nav.ItemInvoked += Nav_ItemInvoked;
        nav.BackRequested += Nav_BackRequested;

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
                Icon = new CompatSymbolIcon()
                {
                    Symbol = FluentIcons.Common.Symbol.PuzzlePiece
                }
            },
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.BackupObservable.ToBinding(),
                Tag = typeof(BackupSettingsPage),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.CloudBackup
                }
            },
            new NavigationViewItem()
            {
                [!ContentProperty] = S.SettingsPage.InfoObservable.ToBinding(),
                Tag = typeof(InfomationPage),
                Icon = new CompatSymbolIcon()
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

    private void Frame_Navigated(object sender, FluentAvalonia.UI.Navigation.NavigationEventArgs e)
    {
        if (e.Content is AccountSettingsPage account
            && DataContext is SettingsPageViewModel settingsPage)
        {
            account.DataContext = settingsPage.Account;
        }

        foreach (NavigationViewItem nvi in nav.MenuItems)
        {
            if (nvi.Tag is Type tag && tag == e.SourcePageType)
            {
                nav.SelectedItem = nvi;
                return;
            }
        }
    }
}
