using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public interface IParsableEditorViewModel
{
    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactiveProperty<string> Value { get; }

    string Header { get; }

    ReactivePropertySlim<string?> Description { get; }

    bool IsDisposed { get; }

    void SetValueString(string? s);

    void SetCurrentValueString(string? s);
}

public sealed class ParsableEditorViewModel<T> : BaseEditorViewModel<T>, IParsableEditorViewModel
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
        if (T.TryParse(s, CultureInfo.CurrentUICulture, out T? newValue))
        {
            SetValue(newValue);
        }
    }

    public void SetCurrentValueString(string? s)
    {
        if (T.TryParse(s, CultureInfo.CurrentUICulture, out T? newValue))
        {
            SetCurrentValueAndGetCoerced(newValue);
        }
    }
}
