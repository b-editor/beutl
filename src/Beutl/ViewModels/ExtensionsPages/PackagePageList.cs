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

    // The start index of the next page: the packages shown so far, excluding the trailing LoadMoreItem.
    public static int NextPageStart(AvaloniaList<object> items)
    {
        return EndsWithLoadMoreItem(items) ? items.Count - 1 : items.Count;
    }

    // Replaces the trailing LoadMoreItem only once the next page has loaded, so a failed request leaves
    // the tile in place to retry.
    public static void AppendNextPage(AvaloniaList<object> items, Package[] page)
    {
        if (EndsWithLoadMoreItem(items))
        {
            items.RemoveAt(items.Count - 1);
        }

        AppendPage(items, page);
    }

    private static void AppendPage(AvaloniaList<object> items, Package[] page)
    {
        items.AddRange(page);

        if (page.Length == PageSize)
        {
            items.Add(new LoadMoreItem());
        }
    }

    private static bool EndsWithLoadMoreItem(AvaloniaList<object> items)
    {
        return items.Count > 0 && items[^1] is LoadMoreItem;
    }
}
