using System.Reactive.Linq;
using System.Text.Json;
using Beutl.Configuration;
using Beutl.Media;
using DynamicData;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public class FontFamilyPickerFlyoutViewModel
{
    private readonly PinnableLibraryItem[] _items;
    private readonly List<FontFamily> _pinnedItems;

    public FontFamilyPickerFlyoutViewModel()
    {
        string json = Preferences.Default.Get("FontManager.PinnedItems", "[]");
        _pinnedItems = (JsonSerializer.Deserialize<string[]>(json) ?? [])
            .Where(s => s != null)
            .Select(s => new FontFamily(s))
            .ToList()!;

        _items = FontManager.Instance.FontFamilies
            .Select(v => new PinnableLibraryItem(v.Name, false, v))
            .ToArray();
        ShowAll.Subscribe(_ => ProcessSearchText());

        // SearchBoxが並列で変更された場合、最後の一つを処理する
        SearchText
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => ProcessSearchText());
    }

    public ReactiveCollection<PinnableLibraryItem> Items { get; } = [];

    public ReactiveProperty<bool> ShowAll { get; } = new();

    public ReactiveProperty<string?> SearchText { get; } = new();

    public ReactiveProperty<PinnableLibraryItem?> SelectedItem { get; } = new();

    public void Pin(PinnableLibraryItem item)
    {
        if (item.UserData is not FontFamily font) return;

        _pinnedItems.Add(font);
        string[] array = _pinnedItems
            .Select(f => f.Name)
            .ToArray();
        Preferences.Default.Set("FontManager.PinnedItems", JsonSerializer.Serialize(array));
        ProcessSearchText();
    }

    public void Unpin(PinnableLibraryItem item)
    {
        if (item.UserData is not FontFamily font) return;

        _pinnedItems.Remove(font);
        string[] array = _pinnedItems
            .Select(f => f.Name)
            .ToArray();
        Preferences.Default.Set("FontManager.PinnedItems", JsonSerializer.Serialize(array));
        ProcessSearchText();
    }

    private bool IsPinned(FontFamily item)
    {
        return _pinnedItems.Contains(item);
    }

    private void ProcessSearchText()
    {
        Items.ClearOnScheduler();
        var items = _items;
        items = items.Select(i => new PinnableLibraryItem(i.DisplayName, IsPinned((FontFamily)i.UserData), i.UserData))
            .OrderByDescending(t => t.IsPinned)
            .ToArray();

        if (string.IsNullOrWhiteSpace(SearchText.Value))
        {
            Items.AddRange(items);
        }
        else
        {
            string[] segments = SearchText.Value.Split(' ')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            var newItems = items.Where(x =>
                    segments.Any(item => x.DisplayName.Contains(item, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(t => t.IsPinned)
                .ToArray();
            Items.AddRange(newItems);
        }
    }
}
