using BeUtl.Framework;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class OpacityEditorViewModel : BaseEditorViewModel<float>
{
    public OpacityEditorViewModel(IAbstractProperty<float> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> Value { get; }
}
