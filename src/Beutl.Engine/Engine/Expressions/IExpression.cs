namespace Beutl.Engine.Expressions;

public interface IExpression<out T> : IExpression
{
    T Evaluate(ExpressionContext context);
}

public interface IExpression
{
    string ExpressionString { get; }

    bool Validate(out string? error);
}
