using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public class ValueEditorViewModel<T> : BaseEditorViewModel<T>
{
    public ValueEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<T> Value { get; }
}
