using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class StringEditorViewModel : BaseEditorViewModel<string>
{
    public StringEditorViewModel(IWrappedProperty<string> property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x ?? string.Empty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<string> Value { get; }
}
