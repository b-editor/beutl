using BeUtl.Services.Editors;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class NumberEditorViewModel<T> : BaseEditorViewModel<T>, INumberEditorViewModel<T>
    where T : struct
{
    public NumberEditorViewModel(IWrappedProperty<T> property)
        : base(property)
    {
        Text = property.GetObservable()
            .Select(x => Format(x))
            .ToReadOnlyReactivePropertySlim(Format(property.GetValue()))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string> Text { get; }

    public INumberEditorService<T> EditorService { get; } = NumberEditorService.Instance.Get<T>();

    private static string Format(T value)
    {
        return value.ToString() ?? string.Empty;
    }
}
