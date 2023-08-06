using Beutl.Graphics.Effects;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class SelectFilterEffectTypeViewModel
{
    private bool _allLoaded;

    public SelectFilterEffectTypeViewModel()
    {
        IReadOnlySet<Type> items = LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.FilterEffect);
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

    public ReactiveCollection<LibraryItem> Items { get; } = new();

    public ReactiveCollection<LibraryItem> AllItems { get; } = new();

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

                Type itemType = typeof(FilterEffect);
                Type[] availableTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .Where(x => !x.IsAbstract
                        && x.IsPublic
                        && x.IsAssignableTo(itemType)
                        && (itemType.GetConstructor(Array.Empty<Type>()) != null
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
                            KnownLibraryItemFormats.FilterEffect,
                            type,
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
