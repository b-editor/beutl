using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public class SelectLibraryItemDialogViewModel
{
    private readonly string _format;
    private readonly Type _baseType;
    private bool _allLoaded;

    public SelectLibraryItemDialogViewModel(string format, Type baseType, string title)
    {
        _format = format;
        _baseType = baseType;
        Title = title;
        IReadOnlySet<Type> items = LibraryService.Current.GetTypesFromFormat(_format);
        Task.Run(() =>
        {
            try
            {
                IsBusy.Value = true;
                foreach (Type type in items)
                {
                    LibraryItem? item = LibraryService.Current.FindItem(type);
                    if (item != null)
                    {
                        Items.Add(item);
                    }
                }
            }
            finally
            {
                IsBusy.Value = false;
            }
        });
    }

    public string Title { get; }

    public ReactiveCollection<LibraryItem> Items { get; } = [];

    public ReactiveCollection<LibraryItem> AllItems { get; } = [];

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public ReactiveProperty<LibraryItem?> SelectedItem { get; } = new();

    public void LoadAllItems()
    {
        if (_allLoaded)
            return;

        Task.Run(() =>
        {
            try
            {
                IsBusy.Value = true;

                Type itemType = _baseType;
                Type[] availableTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .Where(x => !x.IsAbstract
                        && x.IsPublic
                        && x.IsAssignableTo(itemType)
                        && (itemType.GetConstructor([]) != null
                        || itemType.GetConstructors().Length == 0))
                    .ToArray();

                foreach (Type type in availableTypes)
                {
                    LibraryItem? item = LibraryService.Current.FindItem(type);
                    if (item != null)
                    {
                        AllItems.Add(item);
                    }
                    else
                    {
                        AllItems.Add(new SingleTypeLibraryItem(
                            _format, type,
                            type.FullName ?? type.Name));
                    }
                }
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        _allLoaded = true;
    }
}
