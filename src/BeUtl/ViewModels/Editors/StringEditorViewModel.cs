using System.Reactive.Linq;

using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class StringEditorViewModel : BaseEditorViewModel<string>
{
    public StringEditorViewModel(PropertyInstance<string> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .Select(x => x ?? string.Empty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<string> Value { get; }
}
