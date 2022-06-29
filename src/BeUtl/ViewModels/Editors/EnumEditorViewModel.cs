using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T> : BaseEditorViewModel<T>
    where T : struct, Enum
{
    public EnumEditorViewModel(IWrappedProperty<T> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T> Value { get; }
}
