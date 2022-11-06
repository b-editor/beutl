using Beutl.Framework;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

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
