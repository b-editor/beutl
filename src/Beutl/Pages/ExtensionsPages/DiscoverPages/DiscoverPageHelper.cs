using Avalonia.Controls;
using Avalonia.LogicalTree;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

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
}
