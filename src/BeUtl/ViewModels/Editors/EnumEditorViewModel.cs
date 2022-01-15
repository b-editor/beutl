using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T> : BaseEditorViewModel<T>
    where T : struct, Enum
{
    public EnumEditorViewModel(Setter<T> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T> Value { get; }
}
