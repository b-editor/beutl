using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;

namespace Beutl.PropertyAdapters;

// The expression members of SimplePropertyAdapter and AnimatablePropertyAdapter, which IExpressionProperty<T> provides.
public abstract class ExpressionPropertyAdapterBase<T>(IExpressionProperty<T> property, EngineObject obj)
    : EnginePropertyAdapter<T>(property, obj), IExpressionPropertyAdapter<T>
{
    public new IExpressionProperty<T> Property { get; } = property;

    public IExpression<T>? Expression
    {
        get => Property.HasExpression ? Property.Expression : null;
        set => Property.Expression = value;
    }

    public bool HasExpression => Property.HasExpression;

    [field: AllowNull]
    public IObservable<IExpression<T>?> ObserveExpression => field ??= Observable.FromEvent<IExpression<T>?>(
            handler => Property.ExpressionChanged += handler,
            handler => Property.ExpressionChanged -= handler)
        .Select(_ => Expression)
        .DistinctUntilChanged()
        .Publish(Expression)
        .RefCount();
}
