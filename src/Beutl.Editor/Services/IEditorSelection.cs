using Beutl.Collections;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IEditorSelection
{
    IReadOnlyReactiveProperty<CoreObject?> SelectedObject { get; }

    IReadOnlyReactiveProperty<int?> SelectedLayerNumber { get; }

    // Kept while the timeline is closed. Selecting an object outside it replaces it with that object.
    ICoreList<Element> SelectedElements { get; }

    // Runs on the UI thread, posted there from other threads. notifyIfSelected notifies again when obj is selected.
    void Select(CoreObject? obj, bool notifyIfSelected = false);
}
