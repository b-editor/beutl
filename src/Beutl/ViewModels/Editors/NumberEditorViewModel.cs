using Beutl.Framework;
using Beutl.Services.Editors;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class NumberEditorViewModel<T> : BaseEditorViewModel<T>, INumberEditorViewModel<T>
    where T : struct
{
    public NumberEditorViewModel(IAbstractProperty<T> property)
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
