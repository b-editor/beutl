using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;

namespace Beutl.Operation;

public sealed class SimplePropertyAdapter<T>(SimpleProperty<T> property, EngineObject obj)
    : EnginePropertyAdapter<T>(property, obj), IExpressionPropertyAdapter<T>
{
    public IExpression<T>? Expression
    {
        get => Property.Expression;
        set => Property.Expression = value;
    }

    public bool HasExpression => Property.HasExpression;

    [field: AllowNull]
    public IObservable<IExpression<T>?> ObserveExpression => field ??= Observable.FromEvent<IExpression<T>?>(
            handler => Property.ExpressionChanged += handler,
            handler => Property.ExpressionChanged -= handler)
        .Publish(Expression)
        .RefCount();
}
