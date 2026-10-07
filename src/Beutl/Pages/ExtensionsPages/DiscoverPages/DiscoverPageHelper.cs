using Avalonia.Controls;
using Avalonia.LogicalTree;
using Beutl.Api.Objects;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.UI.Controls;

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

    // Opens the details of the package card that was clicked; false when the sender is not a package card.
    public static bool TryNavigateToPackage(Control page, object? sender)
    {
        if (sender is Button { DataContext: Package package }
            && page.FindLogicalAncestorOfType<FAFrame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), package);
            return true;
        }

        return false;
    }
}
