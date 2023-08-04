using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public interface INavigationButtonViewModel
{
    string Header { get; }

    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactivePropertySlim<bool> IsSet { get; }

    ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    bool CanWrite { get; }
}

public sealed class NavigationButtonViewModel<T> : BaseEditorViewModel<T>, INavigationButtonViewModel
    where T : ICoreObject
{
    public NavigationButtonViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        CanWrite = !property.IsReadOnly;

        IsSet = property.GetObservable()
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsNotSetAndCanWrite = IsSet.Select(x => !x && CanWrite)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T?> Value { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSet { get; }

    public ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    public bool CanWrite { get; }
}
