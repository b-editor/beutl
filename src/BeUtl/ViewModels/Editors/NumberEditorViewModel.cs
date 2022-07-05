using BeUtl.Services.Editors;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Streaming;

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

    public override EditorViewModelDescription Description => base.Description with { NumberEditorService = EditorService };

    private string Format(T value)
    {
        if (WrappedProperty is SetterDescription<T>.InternalSetter { Description.Formatter: { } formatter })
        {
            return formatter(value);
        }
        else
        {
            return value.ToString() ?? string.Empty;
        }
    }
}
