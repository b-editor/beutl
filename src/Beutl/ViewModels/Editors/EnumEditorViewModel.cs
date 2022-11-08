using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

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
