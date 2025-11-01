using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Engine;

namespace Beutl.Extensibility;

public interface IPropertyAdapter
{
    Type ImplementedType { get; }

    Type PropertyType { get; }

    string DisplayName { get; }

    string? Description { get; }

    bool IsReadOnly { get; }

    object? GetDefaultValue();

    CoreProperty? GetCoreProperty() => null;

    IProperty? GetEngineProperty() => null;

    Attribute[] GetAttributes()
    {
        var coreProperty = GetCoreProperty();
        if (coreProperty != null)
        {
            var metadata = coreProperty.GetMetadata<CorePropertyMetadata>(ImplementedType);
            return metadata.Attributes;
        }

        var engineProperty = GetEngineProperty();
        if (engineProperty != null)
        {
            return engineProperty.GetPropertyInfo()?.GetCustomAttributes(true).OfType<Attribute>().ToArray() ?? [];
        }

        return [];
    }

    void SetValue(object? value);

    object? GetValue();

    IObservable<object?> GetObservable();
}

public interface IPropertyAdapter<T> : IPropertyAdapter
{
    void SetValue(T? value);

    new T? GetValue();

    new IObservable<T?> GetObservable();

    void IPropertyAdapter.SetValue(object? value)
    {
        if (value is T typed)
        {
            SetValue(typed);
        }
        else
        {
            SetValue(default);
        }
    }

    object? IPropertyAdapter.GetValue()
    {
        return GetValue();
    }

    IObservable<object?> IPropertyAdapter.GetObservable()
    {
        return GetObservable().Select(x => (object?)x);
    }
}

public interface IAnimatablePropertyAdapter : IPropertyAdapter
{
    IAnimation? Animation { get; set; }

    IObservable<IAnimation?> ObserveAnimation { get; }
}

public interface IAnimatablePropertyAdapter<T> : IPropertyAdapter<T>, IAnimatablePropertyAdapter
{
    new IAnimation<T>? Animation { get; set; }

    new IObservable<IAnimation<T>?> ObserveAnimation { get; }

    IAnimation? IAnimatablePropertyAdapter.Animation
    {
        get => Animation;
        set => Animation = value as IAnimation<T>;
    }

    IObservable<IAnimation?> IAnimatablePropertyAdapter.ObserveAnimation => ObserveAnimation;
}

public interface IRemotePropertyAdapter
{
    string DisplayName { get; }

    string? Description { get; }

    Type PropertyType { get; }

    ValueTask SetValue(object? value);

    IObservable<object?> GetObservable();
}

public interface IRemotePropertyAdapter<T> : IRemotePropertyAdapter
{
    ValueTask SetValue(T? value);

    new IObservable<T?> GetObservable();

    async ValueTask IRemotePropertyAdapter.SetValue(object? value)
    {
        if (value is T typed)
        {
            await SetValue(typed);
        }
        else
        {
            await SetValue(default);
        }
    }

    IObservable<object?> IRemotePropertyAdapter.GetObservable()
    {
        return GetObservable().Select(x => (object?)x);
    }
}

public interface IAnimatableRemotePropertyAdapter : IRemotePropertyAdapter
{
    ValueTask SetAnimation(IAnimation? animation);

    IObservable<IAnimation?> ObserveAnimation { get; }
}

public interface IAnimatableRemotePropertyAdapter<T> : IRemotePropertyAdapter<T>, IAnimatableRemotePropertyAdapter
{
    new IObservable<IAnimation<T>?> ObserveAnimation { get; }

    IObservable<IAnimation?> IAnimatableRemotePropertyAdapter.ObserveAnimation => ObserveAnimation;

    async ValueTask IAnimatableRemotePropertyAdapter.SetAnimation(IAnimation? animation)
    {
        await SetAnimation(animation as IAnimation<T>);
    }

    ValueTask SetAnimation(IAnimation<T>? animation);
}
