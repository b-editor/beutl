using Beutl.Animation;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public class ValueEditorViewModel<T> : BaseEditorViewModel<T>
{
    public ValueEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        Value = EditingKeyFrame
            .Select(x => x?.GetObservable(KeyFrame<T>.ValueProperty))
            .Select(x => x ?? WrappedProperty.GetObservable())
            //https://qiita.com/hiki_neet_p/items/4a8873920b566568d63b
            .Switch()
            .ToReadOnlyReactiveProperty()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactiveProperty<T> Value { get; }
}
