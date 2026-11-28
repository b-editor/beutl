using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// What the library item picker needs from a view model, whether it lists library types or files of its own.
internal interface ILibraryItemPickerViewModel : ISearchablePickerViewModel
{
    IReadOnlyList<string> Categories { get; }

    ReactiveProperty<int> SelectedCategoryIndex { get; }

    ReactiveProperty<bool> ShowAll { get; }

    bool HasReferencesTab { get; }

    bool ShowsOnlyReferences { get; }

    ReactiveCollection<PinnableLibraryItem> ReferenceItems { get; }

    bool HasThumbnails { get; }

    ReactiveProperty<bool> ShowTiles { get; }

    ReactiveProperty<bool> IsBusy { get; }
}
