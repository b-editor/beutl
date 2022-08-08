using BeUtl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T> : BaseEditorViewModel<T>
    where T : struct, Enum
{
    public EnumEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T> Value { get; }
}
