using System.Reactive.Linq;

using Beutl.Animation;

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
