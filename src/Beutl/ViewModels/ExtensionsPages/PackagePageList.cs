using Avalonia.Collections;
using Beutl.Api.Objects;

namespace Beutl.ViewModels.ExtensionsPages;

// Paging of the extension store's package lists: pages of PageSize packages, followed by a LoadMoreItem
// while the last page came back full.
internal static class PackagePageList
{
    public const int PageSize = 30;

    public static void ShowFirstPage(AvaloniaList<object> items, Package[] page)
    {
        items.Clear();
        AppendPage(items, page);
    }

    public static void AppendPage(AvaloniaList<object> items, Package[] page)
    {
        items.AddRange(page);

        if (page.Length == PageSize)
        {
            items.Add(new LoadMoreItem());
        }
    }

    public static void RemoveLoadMoreItem(AvaloniaList<object> items)
    {
        items.RemoveAt(items.Count - 1);
    }
}
