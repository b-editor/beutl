using Beutl.Editor.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public interface IParsableEditorViewModel
{
    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactiveProperty<string> Value { get; }

    string Header { get; }

    ReactivePropertySlim<string?> Description { get; }

    ReactivePropertySlim<string?> HoverInfo { get; }

    bool IsDisposed { get; }

    void SetValueString(string? s);

    void SetCurrentValueString(string? s);
}

public sealed class ParsableEditorViewModel<T> : PropertyEditorViewModel<T>, IParsableEditorViewModel
    where T : IParsable<T>
{
    public ParsableEditorViewModel(IPropertyAdapter<T> property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x?.ToString() ?? "")
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables)!;
    }

    public ReadOnlyReactiveProperty<string> Value { get; }

    public void SetValueString(string? s)
    {
        if (T.TryParse(s, CultureInfo.CurrentCulture, out T? newValue))
        {
            SetValue(newValue);
        }
    }

    public void SetCurrentValueString(string? s)
    {
        if (T.TryParse(s, CultureInfo.CurrentCulture, out T? newValue))
        {
            SetCurrentValueAndGetCoerced(newValue);
        }
    }
}
