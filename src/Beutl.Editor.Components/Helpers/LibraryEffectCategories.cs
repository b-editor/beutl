using Beutl.Services;

namespace Beutl.Editor.Components.Helpers;

internal static class LibraryEffectCategories
{
    public static Dictionary<LibraryItem, string> Collect(IReadOnlyList<LibraryItem> items, string format)
    {
        string? rootName = format switch
        {
            KnownLibraryItemFormats.FilterEffect => GraphicsStrings.FilterEffect,
            KnownLibraryItemFormats.AudioEffect => AudioStrings.AudioEffect,
            _ => null,
        };
        if (rootName == null)
            return [];

        var categories = new Dictionary<LibraryItem, string>();
        string others = Strings.Others;
        Walk(items, null);
        return categories;

        void Walk(IReadOnlyList<LibraryItem> list, string? category)
        {
            foreach (LibraryItem item in list)
            {
                switch (item)
                {
                    case GroupLibraryItem group:
                        string? childCategory = category ?? (group.DisplayName == rootName ? null : group.DisplayName);
                        Walk(group.Items, childCategory);
                        break;
                    case SingleTypeLibraryItem single when single.Format == format:
                    case MultipleTypeLibraryItem multiple when multiple.Types.ContainsKey(format):
                        categories[item] = category ?? others;
                        break;
                }
            }
        }
    }
}
