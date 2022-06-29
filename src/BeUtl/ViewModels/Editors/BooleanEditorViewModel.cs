using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class BooleanEditorViewModel : BaseEditorViewModel<bool>
{
    public BooleanEditorViewModel(IWrappedProperty<bool> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> Value { get; }
}
