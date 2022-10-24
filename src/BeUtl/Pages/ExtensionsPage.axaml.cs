using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.Pages.ExtensionsPages;
using BeUtl.Pages.ExtensionsPages.DevelopPages;
using BeUtl.ViewModels;
using BeUtl.Views;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages;

public sealed partial class ExtensionsPage : UserControl
{
    public ExtensionsPage()
    {
        InitializeComponent();

        List<NavigationViewItem> items = GetItems();
        nav.MenuItems = items;
        NavigationViewItem selected = items[0];

        frame.Navigated += Frame_Navigated;
        frame.Navigating += Frame_Navigating;
        nav.ItemInvoked += Nav_ItemInvoked;
        nav.BackRequested += Nav_BackRequested;

        nav.SelectedItem = selected;

        this.GetObservable(IsVisibleProperty).Subscribe(b =>
        {
            if (b)
            {
                if (nav.SelectedItem is NavigationViewItem selected)
                {
                    frame.Navigate((Type)selected.Tag!);
                }
            }
            else
            {
                frame.SetNavigationState("|\n0\n0");
            }
        });
    }

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<MainView>() is { DataContext: MainViewModel viewModel } mainView)
        {
            viewModel.SelectedPage.Value = viewModel.SettingsPage;
        }
    }

    private static List<NavigationViewItem> GetItems()
    {
        return new List<NavigationViewItem>()
        {
            new NavigationViewItem()
            {
                Content = "Home",
                Tag = typeof(HomePage),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Home
                }
            },
            new NavigationViewItem()
            {
                Content = "Library",
                Tag = typeof(LibraryPage),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Library
                }
            },
            new NavigationViewItem()
            {
                Content = "Develop",
                Tag = typeof(DevelopPage),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Code
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
        if (e.InvokedItemContainer is NavigationViewItem nvi
            && nvi.Tag is Type typ
            && DataContext is ExtensionsPageViewModel viewModel)
        {
            if (typ == typeof(DevelopPage))
            {
                frame.Navigate(typ, viewModel.Develop, e.RecommendedNavigationTransitionInfo);
            }
            else if (typ == typeof(LibraryPage))
            {
                frame.Navigate(typ, viewModel.Library, e.RecommendedNavigationTransitionInfo);
            }
            else if (typ == typeof(HomePage))
            {
                frame.Navigate(typ, viewModel.Home, e.RecommendedNavigationTransitionInfo);
            }
        }
    }

    private void Frame_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.NavigationTransitionInfo is EntranceNavigationTransitionInfo entrance)
        {
            if (e.NavigationMode is NavigationMode.Back)
            {
                entrance.FromHorizontalOffset = -28;
            }
            else if (e.NavigationMode is NavigationMode.Forward or NavigationMode.Refresh)
            {
                entrance.FromHorizontalOffset = 28;
            }
            else
            {
                Type type1 = frame.CurrentSourcePageType;
                Type type2 = e.SourcePageType;
                int num1 = ToNumber(type1);
                int num2 = ToNumber(type2);
                if (num1 > num2)
                {
                    entrance.FromHorizontalOffset = -28;
                }
                else
                {
                    entrance.FromHorizontalOffset = 28;
                }
            }
            entrance.FromVerticalOffset = 0;
        }
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        foreach (NavigationViewItem nvi in nav.MenuItems)
        {
            if (nvi.Tag is Type tag && tag == e.SourcePageType)
            {
                nav.SelectedItem = nvi;
                return;
            }
        }

        foreach (NavigationViewItem nvi in nav.MenuItems)
        {
            if (nvi.Tag is Type tag && e.SourcePageType.Namespace?.EndsWith($"{tag.Name}s") == true)
            {
                nav.SelectedItem = nvi;
                return;
            }
        }
    }

    private static int ToNumber(Type type)
    {
        if (type == typeof(DevelopPage))
            return 0;
        else if (type == typeof(PackageDetailsPage))
            return 1;
        else if (type == typeof(PackageReleasesPage))
            return 2;
        else if (type == typeof(PackageSettingsPage))
            return 2;
        else if (type == typeof(ReleasePage))
            return 3;
        return -1;
    }
}
