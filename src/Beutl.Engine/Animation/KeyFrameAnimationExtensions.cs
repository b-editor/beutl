using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.Animation;

public static class KeyFrameAnimationExtensions
{
    // The keyframes may lie below a user or node-port expression, which wins over them.
    public static KeyFrameAnimation<T>? GetKeyFrameAnimation<T>(this IProperty<T> property)
        => property is IExpressionProperty<T> expressionProperty
            ? ExpressionChain.FindKeyFrames(expressionProperty.Expression) as KeyFrameAnimation<T>
            : null;

    public static IKeyFrameAnimation? GetKeyFrameAnimation(this IProperty property)
        => property is IExpressionProperty expressionProperty
            ? ExpressionChain.FindKeyFrames(expressionProperty.Expression) as IKeyFrameAnimation
            : null;

    public static IPropertyAnimation? GetPropertyAnimation(this IProperty property)
        => property is IExpressionProperty expressionProperty
            ? ExpressionChain.FindKeyFrames(expressionProperty.Expression) as IPropertyAnimation
            : null;

    public static void SetPropertyAnimation(this IProperty property, IPropertyAnimation? animation)
    {
        if (property is IExpressionChainHost host)
            host.SetKeyFrames(animation);
        else if (animation != null)
            throw new NotSupportedException($"Property '{property.Name}' does not hold animations.");
    }

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
