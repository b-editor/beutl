using Beutl.Language;
using Beutl.Services;

namespace Beutl.Editor.Services;

internal readonly record struct LibraryCategoryKey(string? Id, string? DisplayName)
{
    public static LibraryCategoryKey Create(string? id, string? displayName)
        => id == null ? new(null, displayName) : new(id, null);
}

internal sealed record LibraryEffectCategory(string? Id, string Title, string? Description)
{
    public LibraryCategoryKey Key => LibraryCategoryKey.Create(Id, Title);
}

internal static class LibraryEffectCategories
{
    public static Dictionary<LibraryItem, LibraryEffectCategory?> Collect(IReadOnlyList<LibraryItem> items, string format)
    {
        (string? rootId, string? rootName) = format switch
        {
            KnownLibraryItemFormats.FilterEffect => (KnownLibraryGroupIds.FilterEffect, GraphicsStrings.FilterEffect),
            KnownLibraryItemFormats.AudioEffect => (KnownLibraryGroupIds.AudioEffect, AudioStrings.AudioEffect),
            _ => (null, null),
        };
        if (rootId == null)
            return [];

        var categories = new Dictionary<LibraryItem, LibraryEffectCategory?>();
        Walk(items, null);
        return categories;

        void Walk(IReadOnlyList<LibraryItem> list, LibraryEffectCategory? category)
        {
            foreach (LibraryItem item in list)
            {
                switch (item)
                {
                    case GroupLibraryItem group:
                        bool isRoot = group.Id is { } id ? id == rootId : group.DisplayName == rootName;
                        LibraryEffectCategory? childCategory = category;
                        if (childCategory == null && !isRoot)
                            childCategory = new LibraryEffectCategory(group.Id, group.DisplayName, group.Description);

                        Walk(group.Items, childCategory);
                        break;
                    case SingleTypeLibraryItem single when single.Format == format:
                    case MultipleTypeLibraryItem multiple when multiple.Types.ContainsKey(format):
                        categories[item] = category;
                        break;
                }
            }
        }
    }
}
