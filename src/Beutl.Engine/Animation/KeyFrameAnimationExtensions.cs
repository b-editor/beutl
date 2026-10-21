using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.Animation;

public static class KeyFrameAnimationExtensions
{
    // The keyframes may lie below a user or node-port expression, which wins over them.
    public static KeyFrameAnimation<T>? GetKeyFrameAnimation<T>(this IProperty<T> property)
        => ExpressionChain.FindKeyFrames(property.Expression) as KeyFrameAnimation<T>;

    public static IKeyFrameAnimation? GetKeyFrameAnimation(this IProperty property)
        => ExpressionChain.FindKeyFrames(property.Expression) as IKeyFrameAnimation;

    // Puts the keyframes at the bottom of the expression chain, below any user or node-port expression.
    public static void SetKeyFrameAnimation<T>(this IProperty<T> property, KeyFrameAnimation<T>? animation)
        => SetKeyFrameAnimation((IProperty)property, animation);

    public static void SetKeyFrameAnimation(this IProperty property, IKeyFrameAnimation? animation)
    {
        if (property is IExpressionChainHost host)
            host.SetKeyFrames(animation);
        else if (animation != null)
            throw new NotSupportedException($"Property '{property.Name}' does not hold keyframes.");
    }
}
