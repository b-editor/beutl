using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IEditorSelection
{
    IReactiveProperty<CoreObject?> SelectedObject { get; }

    IReadOnlyReactiveProperty<int?> SelectedLayerNumber { get; }
}
