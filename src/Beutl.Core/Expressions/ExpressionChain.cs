
namespace Beutl;

// A property's expression slot holds at most a node-port expression over a user expression over keyframes, each
// keeping the one below it as its Replaced.
internal static class ExpressionChain
{
    public static IExpression? FindKeyFrames(IExpression? chain)
    {
        for (IExpression? expression = chain; expression != null; expression = expression.Replaced)
        {
            if (expression is IKeyFrameExpression) return expression;
        }

        return null;
    }

    // The chain with keyFrames, or none, at its bottom. An expression that cannot keep another one ends the chain.
    public static IExpression<T>? WithKeyFrames<T>(IExpression<T>? chain, IExpression<T>? keyFrames)
    {
        if (chain is null or IKeyFrameExpression) return keyFrames;
        if (chain is not IReplacingExpression<T> replacing) return chain;

        IExpression<T>? below = WithKeyFrames(chain.Replaced, keyFrames);
        return ReferenceEquals(below, chain.Replaced) ? chain : replacing.WithReplaced(below);
    }
}

// Marks keyframes, which Beutl.Engine implements, as the bottom of an expression chain.
internal interface IKeyFrameExpression : IExpression;

// Implemented by the properties that hold an expression chain.
internal interface IExpressionChainHost
{
    // Sets the chain as it is, as history replay must, without keeping the keyframes below a chain that has none.
    void SetExpressionChain(IExpression? chain);

    void SetKeyFrames(IExpression? keyFrames);
}
