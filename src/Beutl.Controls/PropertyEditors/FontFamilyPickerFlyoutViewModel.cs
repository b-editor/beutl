using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Text.Json;
using Beutl.Configuration;
using Beutl.Media;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public class FontFamilyPickerFlyoutViewModel
{
    private const string PinnedItemsKey = "FontManager.PinnedItems";
    private static readonly TimeSpan s_searchDelay = TimeSpan.FromMilliseconds(100);

    private readonly Entry[] _entries;
    private readonly List<FontFamily> _pinnedItems;
    // The font last highlighted. A search that matches nothing clears the selection but keeps this,
    // so clearing the search highlights it again.
    private FontFamily? _highlightedFont;
    private string? _appliedSearchText;

    public FontFamilyPickerFlyoutViewModel()
        : this(FontManager.Instance.FontFamilies, s_searchDelay)
    {
    }

    // searchDelay waits for typing to pause before the list is rebuilt; zero applies every change at once.
    internal FontFamilyPickerFlyoutViewModel(IEnumerable<FontFamily> fontFamilies, TimeSpan searchDelay)
    {
        string json = Preferences.Default.Get(PinnedItemsKey, "[]");
        _pinnedItems = (JsonSerializer.Deserialize<string[]>(json) ?? [])
            .Where(s => s != null)
            .Select(s => new FontFamily(s))
            .ToList();

        _entries = fontFamilies
            .Select(v => new Entry(
                v,
                FontFamilyNames.GetDisplayName(v),
                new FontFamilySearchKey(FontFamilyNames.GetSearchNames(v))))
            .OrderBy(i => i.DisplayName)
            .ToArray();

        SelectedItem.Subscribe(item =>
        {
            if (item?.UserData is FontFamily font)
            {
                _highlightedFont = font;
            }
        });
        ShowAll.Subscribe(_ => UpdateItems());

        IObservable<string?> searches = SearchText.Skip(1);
        if (searchDelay > TimeSpan.Zero)
        {
            // SearchBoxが並列で変更された場合、最後の一つを処理する
            searches = searches
                .Throttle(searchDelay)
                .ObserveOnUIDispatcher();
        }

        searches.Subscribe(_ => FlushSearch());
    }

    public ReactiveCollection<PinnableLibraryItem> Items { get; } = new ResettableCollection();

    public ReactiveProperty<bool> ShowAll { get; } = new();

    public ReactiveProperty<string?> SearchText { get; } = new();

    public ReactiveProperty<PinnableLibraryItem?> SelectedItem { get; } = new();

    // Applies the search text right away instead of after the typing pause, so that a key acting on the
    // results, such as Enter, sees the results for what was typed.
    public void FlushSearch()
    {
        if (!string.Equals(SearchText.Value, _appliedSearchText, StringComparison.Ordinal))
        {
            UpdateItems();
        }
    }

    public void Pin(PinnableLibraryItem item)
    {
        if (item.UserData is not FontFamily font) return;

        _pinnedItems.Add(font);
        SavePinnedItems();
        UpdateItems();
    }

    public void Unpin(PinnableLibraryItem item)
    {
        if (item.UserData is not FontFamily font) return;

        _pinnedItems.Remove(font);
        SavePinnedItems();
        UpdateItems();
    }

    private void SavePinnedItems()
    {
        string[] array = _pinnedItems
            .Select(f => f.Name)
            .ToArray();
        Preferences.Default.Set(PinnedItemsKey, JsonSerializer.Serialize(array));
    }

    private bool IsPinned(FontFamily item)
    {
        return _pinnedItems.Contains(item);
    }

    // A query applied for the first time moves the highlight to its best match, whatever rebuilt the list:
    // pinning a font while the typing pause runs applies the new query too. Otherwise the highlighted font
    // stays highlighted.
    private void UpdateItems()
    {
        bool highlightBestMatch = !string.Equals(SearchText.Value, _appliedSearchText, StringComparison.Ordinal);
        _appliedSearchText = SearchText.Value;
        var query = FontFamilySearchQuery.Parse(_appliedSearchText);
        PinnableLibraryItem[] items;
        if (query.IsEmpty)
        {
            items = _entries
                .Select(CreateItem)
                .OrderByDescending(t => t.IsPinned)
                .ToArray();
        }
        else
        {
            // The best match comes first even over a pinned font, since Enter picks the first result.
            items = _entries
                .Select(e => (Entry: e, Score: query.Score(e.SearchKey)))
                .Where(t => t.Score != FontFamilySearchQuery.NoMatch)
                .Select(t => (Item: CreateItem(t.Entry), t.Score))
                .OrderByDescending(t => t.Score)
                .ThenByDescending(t => t.Item.IsPinned)
                .Select(t => t.Item)
                .ToArray();
        }

        FontFamily? highlighted = _highlightedFont;
        ((ResettableCollection)Items).Reset(items);

        SelectedItem.Value = highlightBestMatch && !query.IsEmpty
            ? items.FirstOrDefault()
            : items.FirstOrDefault(i => Equals(i.UserData, highlighted));
    }

    private PinnableLibraryItem CreateItem(Entry entry)
    {
        return new PinnableLibraryItem(entry.DisplayName, IsPinned(entry.Font), entry.Font);
    }

    private sealed record Entry(FontFamily Font, string DisplayName, FontFamilySearchKey SearchKey);

    // Replaces every item with one Reset notification. Adding thousands of fonts one by one makes the list
    // handle each insertion, which takes a noticeable pause on every keystroke.
    private sealed class ResettableCollection : ReactiveCollection<PinnableLibraryItem>
    {
        public void Reset(IEnumerable<PinnableLibraryItem> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (PinnableLibraryItem item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
