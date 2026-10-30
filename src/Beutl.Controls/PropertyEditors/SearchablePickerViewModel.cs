using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Text.Json;
using Beutl.Configuration;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Controls.PropertyEditors;

// An item SearchablePickerViewModel lists: the value it picks, the name it shows, the name it is pinned by, what a
// search matches it against, and a description shown with it.
public sealed record SearchablePickerEntry<TSearchKey>(
    object Value,
    string DisplayName,
    string PinName,
    TSearchKey SearchKey,
    string? Description = null);

// What SearchablePickerFlyout shows and edits.
public interface ISearchablePickerViewModel
{
    ReactiveCollection<PinnableLibraryItem> Items { get; }

    ReactiveProperty<string?> SearchText { get; }

    ReactiveProperty<PinnableLibraryItem?> SelectedItem { get; }

    void FlushSearch();

    void Pin(PinnableLibraryItem item);

    void Unpin(PinnableLibraryItem item);
}

// A list of entries with the pinned ones first, which a search narrows to its matches, best first. The pins are kept
// in Preferences under pinnedItemsKey, so they last until the next start.
public abstract class SearchablePickerViewModel<TSearchKey> : ISearchablePickerViewModel
{
    private readonly string _pinnedItemsKey;
    private readonly SearchablePickerEntry<TSearchKey>[] _entries;
    private readonly List<string> _pinnedNames;
    // The value last highlighted. A search that matches nothing clears the selection but keeps this,
    // so clearing the search highlights it again.
    private object? _highlighted;
    private string? _appliedSearchText;

    // searchDelay waits for typing to pause before the list is rebuilt; zero applies every change at once.
    protected SearchablePickerViewModel(
        IEnumerable<SearchablePickerEntry<TSearchKey>> entries, string pinnedItemsKey, TimeSpan searchDelay)
    {
        _pinnedItemsKey = pinnedItemsKey;
        string json = Preferences.Default.Get(pinnedItemsKey, "[]");
        _pinnedNames = (JsonSerializer.Deserialize<string[]>(json) ?? [])
            .Where(s => s != null)
            .ToList();
        _entries = entries.ToArray();

        SelectedItem.Subscribe(item =>
        {
            if (item != null)
            {
                _highlighted = item.UserData;
            }
        });
        UpdateItems();

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
        if (FindEntry(item) is not { } entry) return;

        _pinnedNames.Add(entry.PinName);
        SavePinnedItems();
        UpdateItems();
    }

    public void Unpin(PinnableLibraryItem item)
    {
        if (FindEntry(item) is not { } entry) return;

        _pinnedNames.Remove(entry.PinName);
        SavePinnedItems();
        UpdateItems();
    }

    // Returns how well each search key matches searchText, higher first and null for no match, or null when the text
    // searches nothing. Never called with an empty text.
    protected abstract Func<TSearchKey, int?>? Search(string searchText);

    private SearchablePickerEntry<TSearchKey>? FindEntry(PinnableLibraryItem item)
    {
        return Array.Find(_entries, e => Equals(e.Value, item.UserData));
    }

    private void SavePinnedItems()
    {
        Preferences.Default.Set(_pinnedItemsKey, JsonSerializer.Serialize(_pinnedNames));
    }

    // A query applied for the first time moves the highlight to its best match, whatever rebuilt the list:
    // pinning an item while the typing pause runs applies the new query too. Otherwise the highlighted item
    // stays highlighted.
    private void UpdateItems()
    {
        bool highlightBestMatch = !string.Equals(SearchText.Value, _appliedSearchText, StringComparison.Ordinal);
        _appliedSearchText = SearchText.Value;
        Func<TSearchKey, int?>? search = string.IsNullOrEmpty(_appliedSearchText) ? null : Search(_appliedSearchText);
        PinnableLibraryItem[] items;
        if (search == null)
        {
            items = _entries
                .Select(CreateItem)
                .OrderByDescending(t => t.IsPinned)
                .ToArray();
        }
        else
        {
            // The best match comes first even over a pinned item, since Enter picks the first result.
            items = _entries
                .Select(e => (Entry: e, Score: search(e.SearchKey)))
                .Where(t => t.Score.HasValue)
                .Select(t => (Item: CreateItem(t.Entry), Score: t.Score!.Value))
                .OrderByDescending(t => t.Score)
                .ThenByDescending(t => t.Item.IsPinned)
                .Select(t => t.Item)
                .ToArray();
        }

        object? highlighted = _highlighted;
        ((ResettableCollection)Items).Reset(items);

        SelectedItem.Value = highlightBestMatch && search != null
            ? items.FirstOrDefault()
            : items.FirstOrDefault(i => Equals(i.UserData, highlighted));
    }

    private PinnableLibraryItem CreateItem(SearchablePickerEntry<TSearchKey> entry)
    {
        return new PinnableLibraryItem(
            entry.DisplayName, _pinnedNames.Contains(entry.PinName), entry.Value, entry.Description);
    }

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
