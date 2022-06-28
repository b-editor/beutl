using System.Collections;

using BeUtl.ProjectSystem;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class LayerOpsEditorViewModel : BaseEditorViewModel
{
    public LayerOpsEditorViewModel(IWrappedProperty property)
        : base(property)
    {
        List = property.GetObservable()
            .Select(x => x as IList<LayerOperation>)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<IList<LayerOperation>?> List { get; }
}
