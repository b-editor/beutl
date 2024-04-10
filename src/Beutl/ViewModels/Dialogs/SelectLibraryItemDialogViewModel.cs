using System.Text.RegularExpressions;
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
    private readonly Task<LibraryItem[]> _itemsTask;
    private Task<LibraryItem[]>? _allItemsTask;

    public SelectLibraryItemDialogViewModel(string format, Type baseType)
    {
        _format = format;
        _baseType = baseType;
        IReadOnlySet<Type> items = LibraryService.Current.GetTypesFromFormat(_format);

        _itemsTask = Task.Run(() =>
        {
            try
            {
                IsBusy.Value = true;
                return items.Select(i => LibraryService.Current.FindItem(i))
                    .Where(i => i != null)
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

    public ReactiveCollection<LibraryItem> Items { get; } = [];

    public ReactiveProperty<bool> ShowAll { get; } = new();

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public ReactiveProperty<string?> SearchText { get; } = new();

    public ReactiveProperty<LibraryItem?> SelectedItem { get; } = new();

    public Task<LibraryItem[]> LoadAllItems()
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
                        return item ?? new SingleTypeLibraryItem(
                            _format, type,
                            type.FullName ?? type.Name);
                    })
                    .ToArray();
            }
            finally
            {
                IsBusy.Value = false;
            }
        });
    }

    private async void ProcessSearchText()
    {
        Items.ClearOnScheduler();
        if (ShowAll.Value)
        {
            Items.AddRange(await LoadAllItems());
        }
        else
        {
            Items.AddRange(await _itemsTask);
        }

        if (string.IsNullOrWhiteSpace(SearchText.Value)) return;
        Regex[] regexes = RegexHelper.CreateRegexes(SearchText.Value);

        var newItems = Items.Select(v => LibraryItemViewModel.CreateFromOperatorRegistryItem(v))
            .Select(v => (score: v.Match(regexes), item: v))
            .Where(v => v.score > 0)
            .OrderByDescending(v => v.score)
            .Select(v => (LibraryItem)v.item.Data!)
            .ToArray();
        Items.Clear();
        Items.AddRange(newItems);
    }
}
