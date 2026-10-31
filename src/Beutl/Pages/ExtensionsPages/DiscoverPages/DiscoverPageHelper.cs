using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.Api.Objects;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.Pages.ExtensionsPages.DiscoverPages;

// Shared by the pages hosted in the extension store's frame.
internal static class DiscoverPageHelper
{
    public static void DestroyDataContext<TViewModel>(Control page)
        where TViewModel : IDisposable
    {
        if (page.DataContext is TViewModel disposable)
        {
            disposable.Dispose();
        }

        page.DataContext = null;
    }

    public static DataContextFactory GetDataContextFactory(Control page)
    {
        return ((ExtensionsPageViewModel)page.FindLogicalAncestorOfType<ExtensionsPage>()!.DataContext!).Discover.DataContextFactory;
    }

    // Handles a click that bubbled up from a package card (PackageCards.axaml): a package card opens its
    // details, and the Show more card loads the next page.
    public static void OnPackageCardClick(Control page, RoutedEventArgs e, AsyncReactiveCommand more)
    {
        if (e.Source is Button { DataContext: Package package }
            && page.FindLogicalAncestorOfType<FAFrame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), package);
            e.Handled = true;
        }
        else if (e.Source is Button { DataContext: LoadMoreItem })
        {
            more.Execute();
            e.Handled = true;
        }
    }
}
