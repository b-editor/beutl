using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class BooleanEditorViewModel : BaseEditorViewModel<bool>
{
    public BooleanEditorViewModel(Setter<bool> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<bool> Value { get; }
}
