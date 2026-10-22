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

    IProperty? GetEngineProperty() => null;

    Attribute[] GetAttributes() => GetEngineProperty()?.GetAttributes() ?? [];

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

// The keyframes of the property, which lie at the bottom of its expression chain.
public interface IAnimatablePropertyAdapter : IPropertyAdapter
{
    IKeyFrameAnimation? Animation { get; set; }

    IObservable<IKeyFrameAnimation?> ObserveAnimation { get; }
}

public interface IAnimatablePropertyAdapter<T> : IPropertyAdapter<T>, IAnimatablePropertyAdapter, IExpressionPropertyAdapter<T>
{
    new KeyFrameAnimation<T>? Animation { get; set; }

    new IObservable<KeyFrameAnimation<T>?> ObserveAnimation { get; }

    IKeyFrameAnimation? IAnimatablePropertyAdapter.Animation
    {
        get => Animation;
        set => Animation = value as KeyFrameAnimation<T>;
    }

    IObservable<IKeyFrameAnimation?> IAnimatablePropertyAdapter.ObserveAnimation => ObserveAnimation;
}

// The expression over the property's keyframes; the keyframes are IAnimatablePropertyAdapter.Animation.
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
