using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;

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

public interface IAnimatablePropertyAdapter<T> : IPropertyAdapter<T>, IAnimatablePropertyAdapter, IExpressionPropertyAdapter<T>
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

public interface IExpressionPropertyAdapter : IPropertyAdapter
{
    IExpression? Expression { get; set; }

    bool HasExpression { get; }

    IObservable<IExpression?> ObserveExpression { get; }
}

public interface IExpressionPropertyAdapter<T> : IPropertyAdapter<T>, IExpressionPropertyAdapter
{
    new IExpression<T>? Expression { get; set; }

    new IObservable<IExpression<T>?> ObserveExpression { get; }

    IExpression? IExpressionPropertyAdapter.Expression
    {
        get => Expression;
        set => Expression = value as IExpression<T>;
    }

    IObservable<IExpression?> IExpressionPropertyAdapter.ObserveExpression =>
        ObserveExpression.Select(e => (IExpression?)e);
}
