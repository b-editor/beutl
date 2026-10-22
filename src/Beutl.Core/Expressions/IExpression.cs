namespace Beutl;

public interface IExpression<out T> : IExpression
{
    T Evaluate(ExpressionContext context);

    // The expression this one replaced, which the property gets back once this one is removed.
    new IExpression<T>? Replaced => null;

    Type IExpression.ResultType => typeof(T);

    IExpression? IExpression.Replaced => Replaced;
}

public interface IExpression
{
    string ExpressionString { get; }

    Type ResultType { get; }

    IExpression? Replaced => null;

    bool Validate(out string? error);
}

// An expression that can be put over another one, which it keeps as its Replaced.
internal interface IReplacingExpression<T> : IExpression<T>
{
    IExpression<T> WithReplaced(IExpression<T>? replaced);
}
