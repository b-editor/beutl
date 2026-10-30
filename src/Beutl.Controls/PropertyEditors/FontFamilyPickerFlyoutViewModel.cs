using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

internal sealed class FontFamilyPickerFlyoutViewModel : SearchablePickerViewModel<FontFamilySearchKey>
{
    private const string PinnedItemsKey = "FontManager.PinnedItems";
    private static readonly TimeSpan s_searchDelay = TimeSpan.FromMilliseconds(100);

    public FontFamilyPickerFlyoutViewModel()
        : this(FontManager.Instance.FontFamilies, s_searchDelay)
    {
    }

    // searchDelay waits for typing to pause before the list is rebuilt; zero applies every change at once.
    internal FontFamilyPickerFlyoutViewModel(IEnumerable<FontFamily> fontFamilies, TimeSpan searchDelay)
        : base(CreateEntries(fontFamilies), PinnedItemsKey, searchDelay)
    {
    }

    protected override Func<FontFamilySearchKey, int?>? Search(string searchText)
    {
        var query = FontFamilySearchQuery.Parse(searchText);
        if (query.IsEmpty) return null;

        return key => query.Score(key) is var score and not FontFamilySearchQuery.NoMatch ? score : null;
    }

    private static IEnumerable<SearchablePickerEntry<FontFamilySearchKey>> CreateEntries(IEnumerable<FontFamily> fontFamilies)
    {
        return fontFamilies
            .Select(v => new SearchablePickerEntry<FontFamilySearchKey>(
                v,
                FontFamilyNames.GetDisplayName(v),
                v.Name,
                new FontFamilySearchKey(FontFamilyNames.GetSearchNames(v))))
            .OrderBy(i => i.DisplayName);
    }
}
