using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class OpacityEditorViewModel : BaseEditorViewModel<float>
{
    public OpacityEditorViewModel(IWrappedProperty<float> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> Value { get; }
}
