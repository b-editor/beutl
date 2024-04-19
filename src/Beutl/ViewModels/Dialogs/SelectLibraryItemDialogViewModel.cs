using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Services;
using DynamicData;
using NuGet.Packaging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Dialogs;

public class SelectLibraryItemDialogViewModel
{
    private readonly string _format;
    private readonly Type _baseType;
    private readonly Task<PinnableLibraryItem[]> _itemsTask;
    private readonly List<Type> _pinnedItems;
    private Task<PinnableLibraryItem[]>? _allItemsTask;

    public SelectLibraryItemDialogViewModel(string format, Type baseType)
    {
        _format = format;
        _baseType = baseType;
        IReadOnlySet<Type> items = LibraryService.Current.GetTypesFromFormat(_format);

        string json = Preferences.Default.Get("LibraryService.PinnedItems", "[]");
        _pinnedItems = (JsonSerializer.Deserialize<string[]>(json) ?? [])
            .Select(TypeFormat.ToType)
            .Where(t => t != null)
            .ToList()!;

        _itemsTask = Task.Run(() =>
        {
            try
            {
                IsBusy.Value = true;
                return items.Select(i => LibraryService.Current.FindItem(i))
                    .Where(i => i != null)
                    .Select(i => new PinnableLibraryItem(i!.DisplayName, false, i))
                    .ToArray();
            }
            finally
            {
                IsBusy.Value = false;
            }
        })!;
        ShowAll.Subscribe(_ => ProcessSearchText());

        // SearchBoxが並列で変更された場合、最後の一つを処理する
        SearchText
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => ProcessSearchText());
    }

    public ReactiveCollection<PinnableLibraryItem> Items { get; } = [];

    public ReactiveProperty<bool> ShowAll { get; } = new();

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public ReactiveProperty<string?> SearchText { get; } = new();

    public ReactiveProperty<PinnableLibraryItem?> SelectedItem { get; } = new();

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
                                && (itemType.GetConstructor([]) != null
                                    || itemType.GetConstructors().Length == 0))
                    .ToArray();

                return availableTypes
                    .Select(type =>
                    {
                        LibraryItem? item = LibraryService.Current.FindItem(type);
                        item ??= new SingleTypeLibraryItem(
                            _format, type,
                            type.FullName ?? type.Name);
                        return new PinnableLibraryItem(item.DisplayName, false, item);
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
        string[] array = _pinnedItems
            .Select(TypeFormat.ToString)
            .ToArray();
        Preferences.Default.Set("LibraryService.PinnedItems", JsonSerializer.Serialize(array));
        ProcessSearchText();
    }

    public void Unpin(PinnableLibraryItem item)
    {
        Type? type = GetImplementationType((LibraryItem)item.UserData);
        if (type == null) return;

        _pinnedItems.Remove(type);
        string[] array = _pinnedItems
            .Select(TypeFormat.ToString)
            .ToArray();
        Preferences.Default.Set("LibraryService.PinnedItems", JsonSerializer.Serialize(array));
        ProcessSearchText();
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

    private bool IsPinned(LibraryItem item)
    {
        Type? type = GetImplementationType(item);
        if (type == null) return false;
        return _pinnedItems.Contains(type);
    }

    private async void ProcessSearchText()
    {
        Items.ClearOnScheduler();
        var items = ShowAll.Value ? await LoadAllItems() : await _itemsTask;
        items = items.Select(i => new PinnableLibraryItem(i.DisplayName, IsPinned((LibraryItem)i.UserData), i.UserData))
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
                .Select(v => (ViewModel: LibraryItemViewModel.CreateFromOperatorRegistryItem((LibraryItem)v.UserData), IsPinned: v.IsPinned))
                .Select(v => (score: v.ViewModel.Match(regexes), item: v.ViewModel, IsPinned: v.IsPinned))
                .Where(v => v.score > 0)
                .OrderByDescending(t => t.IsPinned)
                .ThenByDescending(v => v.score)
                .Select(v => new PinnableLibraryItem(((LibraryItem)v.item.Data!).DisplayName, v.IsPinned, v.item.Data))
                .ToArray();
            Items.AddRange(newItems);
        }
    }
}
