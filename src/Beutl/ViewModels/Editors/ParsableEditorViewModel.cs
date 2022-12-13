using Beutl.Framework;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public interface IParsableEditorViewModel
{
    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactivePropertySlim<string> Value { get; }

    string Header { get; }
}

public sealed class ParsableEditorViewModel<T> : BaseEditorViewModel<T>, IParsableEditorViewModel
    where T : IParsable<T>
{
    public ParsableEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x?.ToString() ?? "")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<string> Value { get; }
}
