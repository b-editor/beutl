using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.Media.Decoding;
using Beutl.Services;
using DynamicData;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public class SelectLibraryItemDialogViewModel
{
    private readonly LibraryService _libraryService = null!;
    private readonly string _format;
    private readonly Type _baseType;
    private readonly Task<PinnableLibraryItem[]> _itemsTask;
    private readonly List<Type> _pinnedItems;
    private readonly Dictionary<LibraryItem, string> _effectCategories = [];
    private readonly ObservableCollection<string> _categories = [];
    private Task<PinnableLibraryItem[]>? _allItemsTask;
    private PinnableLibraryItem[] _allReferenceItems = [];
    private int _searchVersion;
    private bool _updatingCategories;
    private string? _appliedSearchText;
    private string? _appliedReferenceSearchText;

    public SelectLibraryItemDialogViewModel(Beutl.Media.FontManager fonts, LibraryService libraryService, string format, Type baseType)
        : this(fonts, libraryService, format, baseType, null, null)
    {
    }

    // `thumbnailContent` captures what the tiles show each type on: the object or edge the picked type is added to.
    // Without it, they show the samples.
    internal SelectLibraryItemDialogViewModel(Beutl.Media.FontManager fonts, LibraryService libraryService, string format, Type baseType,
        Func<CancellationToken, Task<LibraryThumbnailSubject?>>? thumbnailContent, DecoderRegistry? decoders = null)
        : this(fonts, libraryService, format, baseType, null, null, thumbnailContent, decoders)
    {
    }

    internal SelectLibraryItemDialogViewModel(Beutl.Media.FontManager fonts, LibraryService libraryService, string format, Type baseType,
        Task<PinnableLibraryItem[]>? itemsTask, Task<PinnableLibraryItem[]>? allItemsTask,
        Func<CancellationToken, Task<LibraryThumbnailSubject?>>? thumbnailContent = null, DecoderRegistry? decoders = null)
    {
        _libraryService = libraryService;
        _format = format;
        _baseType = baseType;
        _allItemsTask = allItemsTask;
        HasThumbnails = LibraryThumbnailRenderer.HasThumbnails(baseType);
        Thumbnails = HasThumbnails ? new LibraryThumbnailSet(new SceneRenderServices(fonts, decoders), thumbnailContent) : null;
        ShowTiles.Value = Preferences.Default.Get(ShowTilesKey, true);
        ShowTiles.Skip(1).Subscribe(value => Preferences.Default.Set(ShowTilesKey, value));
        _effectCategories = LibraryEffectCategories.Collect(libraryService.Items, format);
        string others = Strings.Others;
        if (_effectCategories.Values.Any(category => category != others))
        {
            _categories = [Strings.AllEffectCategories, .. _effectCategories.Values.Distinct().OrderBy(category => category == others)];
            SelectedCategoryIndex.Skip(1).Where(_ => !_updatingCategories).Subscribe(_ => ProcessSearchText());
        }

        IReadOnlySet<Type> items = _libraryService.GetTypesFromFormat(_format);
        if (items.Count == 0)
        {
            ShowAll.Value = true;
        }

        string json = Preferences.Default.Get("LibraryService.PinnedItems", "[]");
        _pinnedItems = (JsonSerializer.Deserialize<string[]>(json) ?? [])
            .Select(TypeFormat.ToType)
            .Where(t => t != null)
            .ToList()!;

        _itemsTask = itemsTask ?? Task.Run(() =>
        {
            try
            {
                IsBusy.Value = true;
                return items.Select(i => _libraryService.FindItem(i))
                    .Where(i => i != null)
                    .Select(i => new PinnableLibraryItem(i!.DisplayName, false, i, i.Description))
                    .ToArray();
            }
            finally
            {
                IsBusy.Value = false;
            }
        });
        ShowAll.Skip(1).Subscribe(_ => ProcessSearchText());

        FlushSearchAfterTypingPause();

        ProcessSearchText();
    }

    // Lists only the given references, for picking the target of a presenter, so no types are looked up.
    internal SelectLibraryItemDialogViewModel(IReadOnlyList<TargetObjectInfo> targets)
    {
        _format = string.Empty;
        _baseType = typeof(object);
        _itemsTask = Task.FromResult<PinnableLibraryItem[]>([]);
        _pinnedItems = [];
        ShowsOnlyReferences = true;

        FlushSearchAfterTypingPause();

        InitializeReferences(targets);
    }

    private const string ShowTilesKey = "LibraryItemPicker.ShowTiles";

    // Whether the listed types have moving thumbnails, which the picker can show as tiles.
    public bool HasThumbnails { get; }

    // Whether the picker shows the items as tiles, remembered across pickers.
    public ReactiveProperty<bool> ShowTiles { get; } = new();

    public ReactiveCollection<PinnableLibraryItem> Items { get; } = [];

    public IReadOnlyList<string> Categories => _categories;

    public ReactiveProperty<int> SelectedCategoryIndex { get; } = new();

    public ReactiveProperty<bool> ShowAll { get; } = new();

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public ReactiveProperty<string?> SearchText { get; } = new();

    public ReactiveProperty<PinnableLibraryItem?> SelectedItem { get; } = new();

    public ReactiveCollection<PinnableLibraryItem> ReferenceItems { get; } = [];

    public bool HasReferencesTab => !ShowsOnlyReferences && _allReferenceItems.Length > 0;

    // The references are shown alone, without the type list or the tab that switches to it.
    public bool ShowsOnlyReferences { get; }

    internal Task SearchTask { get; private set; } = Task.CompletedTask;

    // Applies the search text right away instead of after the typing pause, so that a key acting on the results,
    // such as Down or Enter, sees the results for what was typed. A catalog still loading shows no results until it
    // finishes, so there is nothing stale to act on.
    public void FlushSearch()
    {
        if (!ShowsOnlyReferences && !string.Equals(SearchText.Value, _appliedSearchText, StringComparison.Ordinal))
            ProcessSearchText();
        if (!string.Equals(SearchText.Value, _appliedReferenceSearchText, StringComparison.Ordinal))
            ProcessReferenceSearchText();
    }

    private void FlushSearchAfterTypingPause()
    {
        // SearchBoxが並列で変更された場合、最後の一つを処理する
        SearchText
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => FlushSearch());
    }

    public async Task SelectTypeAsync(Type? type)
    {
        if (type == null) return;

        if (!_libraryService.GetTypesFromFormat(_format).Contains(type))
            ShowAll.Value = true;

        await SearchTask;
        SelectedItem.Value = Items.FirstOrDefault(item => GetImplementationType((LibraryItem)item.UserData) == type);
    }

    public Task<PinnableLibraryItem[]> LoadAllItems()
    {
        return _allItemsTask ??= Task.Run(() =>
        {
            try
            {
                IsBusy.Value = true;

                Type itemType = _baseType;
                Type[] availableTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .Where(x => x is { IsAbstract: false, IsPublic: true }
                                && x.IsAssignableTo(itemType)
                                && (x.IsValueType || x.GetConstructor(Type.EmptyTypes) != null))
                    .ToArray();

                return availableTypes
                    .Select(type =>
                    {
                        LibraryItem? item = _libraryService.FindItem(type);
                        // Keep the concrete type when the library entry binds it under a different format.
                        if (item == null || GetImplementationType(item) != type)
                        {
                            item = new SingleTypeLibraryItem(
                                _format, type, item?.DisplayName ?? TypeDisplayHelpers.GetLocalizedName(type), item?.Description);
                        }
                        return new PinnableLibraryItem(item.DisplayName, false, item, item.Description);
                    })
                    .ToArray();
            }
            finally
            {
                IsBusy.Value = false;
            }
        });
    }

    public void Pin(PinnableLibraryItem item)
    {
        Type? type = GetImplementationType((LibraryItem)item.UserData);
        if (type == null) return;

        _pinnedItems.Add(type);
        SavePinnedItems();
        ProcessSearchText();
    }

    public void Unpin(PinnableLibraryItem item)
    {
        Type? type = GetImplementationType((LibraryItem)item.UserData);
        if (type == null) return;

        _pinnedItems.Remove(type);
        SavePinnedItems();
        ProcessSearchText();
    }

    private void SavePinnedItems()
    {
        string[] array = _pinnedItems
            .Select(TypeFormat.ToString)
            .ToArray();
        Preferences.Default.Set("LibraryService.PinnedItems", JsonSerializer.Serialize(array));
    }

    private Type? GetImplementationType(LibraryItem item)
    {
        return item switch
        {
            SingleTypeLibraryItem single => single.ImplementationType,
            MultipleTypeLibraryItem multi => multi.Types.GetValueOrDefault(_format),
            _ => null
        };
    }

    private IAnimatedThumbnailSource? GetThumbnail(LibraryItem item)
    {
        return Thumbnails != null && GetImplementationType(item) is { } type
            ? Thumbnails.GetSource(type)
            : null;
    }

    // The tiles' thumbnails, while the picker is open.
    internal LibraryThumbnailSet? Thumbnails { get; }

    // Frees the thumbnails once the picker has closed.
    internal void ReleaseThumbnails()
    {
        Thumbnails?.Dispose();
    }

    private bool IsPinned(LibraryItem item)
    {
        Type? type = GetImplementationType(item);
        if (type == null) return false;
        return _pinnedItems.Contains(type);
    }

    public void InitializeReferences(IReadOnlyList<TargetObjectInfo> targets)
    {
        _allReferenceItems = targets
            .Select(t => new PinnableLibraryItem(t.DisplayName, false, t))
            .ToArray();
        ProcessReferenceSearchText();
    }

    private async void ProcessSearchText()
    {
        await (SearchTask = ProcessSearchTextAsync());
    }

    private async Task ProcessSearchTextAsync()
    {
        int searchVersion = Interlocked.Increment(ref _searchVersion);
        Items.ClearOnScheduler();
        var items = ShowAll.Value ? await LoadAllItems() : await _itemsTask;
        if (searchVersion != Volatile.Read(ref _searchVersion)) return;

        string others = Strings.Others;
        UpdateOtherCategory(items, others);

        // Read after the wait, so this is the text the results are for, whichever path asked for them.
        _appliedSearchText = SearchText.Value;
        IEnumerable<PinnableLibraryItem> matchingItems = items;
        int categoryIndex = SelectedCategoryIndex.Value;
        if (categoryIndex > 0 && categoryIndex < Categories.Count)
        {
            string category = Categories[categoryIndex];
            matchingItems = matchingItems.Where(item => GetEffectCategory(item, others) == category);
        }

        items = matchingItems.Select(i => new PinnableLibraryItem(i.DisplayName, IsPinned((LibraryItem)i.UserData), i.UserData, i.Description)
        {
            Thumbnail = GetThumbnail((LibraryItem)i.UserData),
        })
            .OrderByDescending(t => t.IsPinned)
            .ToArray();

        if (string.IsNullOrWhiteSpace(SearchText.Value))
        {
            Items.AddRange(items);
        }
        else
        {
            Regex[] regexes = RegexHelper.CreateRegexes(SearchText.Value);

            var newItems = items
                .Select(v => (score: LibraryItemViewModel.CreateFromLibraryItem((LibraryItem)v.UserData).Match(regexes), item: v))
                .Where(v => v.score > 0)
                .OrderByDescending(t => t.item.IsPinned)
                .ThenByDescending(v => v.score)
                .Select(v => v.item)
                .ToArray();
            Items.AddRange(newItems);
        }
    }

    private string GetEffectCategory(PinnableLibraryItem item, string others)
        => _effectCategories.GetValueOrDefault((LibraryItem)item.UserData, others);

    private void UpdateOtherCategory(PinnableLibraryItem[] items, string others)
    {
        if (_categories.Count == 0)
            return;

        bool hasOthers = _categories[^1] == others;
        bool needsOthers = items.Any(item => GetEffectCategory(item, others) == others);
        if (hasOthers == needsOthers)
            return;

        // The picker can change its selection during this update. The current search reads that selection below.
        _updatingCategories = true;
        try
        {
            if (needsOthers)
            {
                _categories.Add(others);
            }
            else
            {
                if (SelectedCategoryIndex.Value == _categories.Count - 1)
                    SelectedCategoryIndex.Value = 0;

                _categories.RemoveAt(_categories.Count - 1);
            }
        }
        finally
        {
            _updatingCategories = false;
        }
    }

    private void ProcessReferenceSearchText()
    {
        ReferenceItems.ClearOnScheduler();
        _appliedReferenceSearchText = SearchText.Value;

        if (string.IsNullOrWhiteSpace(SearchText.Value))
        {
            ReferenceItems.AddRange(_allReferenceItems);
        }
        else
        {
            string[] segments = SearchText.Value.Split(' ')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            var filtered = _allReferenceItems
                .Where(x => segments.Any(s =>
                    x.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            ReferenceItems.AddRange(filtered);
        }
    }
}
