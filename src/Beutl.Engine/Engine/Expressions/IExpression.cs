namespace Beutl.Engine.Expressions;

public interface IExpression<out T> : IExpression
{
    T Evaluate(ExpressionContext context);

    Type IExpression.ResultType => typeof(T);
}

public interface IExpression
{
    string ExpressionString { get; }

    Type ResultType { get; }

    bool Validate(out string? error);
}
