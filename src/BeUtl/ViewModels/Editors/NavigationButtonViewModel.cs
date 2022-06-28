using System.Reflection;

using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public interface INavigationButtonViewModel
{
}

public sealed class NavigationButtonViewModel<T> : BaseEditorViewModel<T>, INavigationButtonViewModel
    where T : ICoreObject
{
    private static readonly NullabilityInfoContext s_context = new();

    public NavigationButtonViewModel(IWrappedProperty<T> property)
        : base(property)
    {
        CoreProperty<T> coreProperty = property.AssociatedProperty;
        PropertyInfo propertyInfo = coreProperty.OwnerType.GetProperty(coreProperty.Name)!;
        NullabilityInfo? nullabilityInfo = s_context.Create(propertyInfo);

        CanWrite = propertyInfo.SetMethod?.IsPublic == true;
        CanDelete = CanWrite && nullabilityInfo.WriteState == NullabilityState.Nullable;

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

    public bool CanDelete { get; }
}
