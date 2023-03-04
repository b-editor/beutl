using Beutl.Animation;
using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public class ValueEditorViewModel<T> : BaseEditorViewModel<T>
{
    public ValueEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        Value = EditingKeyFrame
            .SelectMany(x => x != null ? x.GetObservable(KeyFrame<T>.ValueProperty) : WrappedProperty.GetObservable())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<T> Value { get; }
}
