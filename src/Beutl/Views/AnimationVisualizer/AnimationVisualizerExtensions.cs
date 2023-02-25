using System.Numerics;

using Avalonia.Controls;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public static class AnimationVisualizerExtensions
{
    public static Control CreateAnimationSpanVisualizer(IAnimation animation, IKeyFrame keyframe)
    {
        if (animation is KeyFrameAnimation<Media.Color> colorAnm
            && keyframe is KeyFrame<Media.Color> colorAnmSpan)
        {
            return new ColorAnimationSpanVisualizer(colorAnm, colorAnmSpan);
        }

        Type type = keyframe.GetType().GetGenericArguments()[0];
        Type numberType = typeof(INumber<>);
        Type minMaxValueType = typeof(IMinMaxValue<>);
        Type binaryIntegerType = typeof(IBinaryInteger<>);
        Type floatingPointType = typeof(IFloatingPoint<>);

        if (IsAssignableToGenericType(type, numberType)
            && IsAssignableToGenericType(type, minMaxValueType))
        {
            if (IsAssignableToGenericType(type, binaryIntegerType))
            {
                return (Control)Activator.CreateInstance(typeof(IntegerAnimationSpanVisualizer<>).MakeGenericType(type), animation, keyframe)!;
            }
            else if (IsAssignableToGenericType(type, floatingPointType))
            {
                return (Control)Activator.CreateInstance(typeof(FloatingPointAnimationSpanVisualizer<>).MakeGenericType(type), animation, keyframe)!;
            }
        }

        return (Control)Activator.CreateInstance(typeof(EasingFunctionSpanVisualizer<>).MakeGenericType(type), animation, keyframe)!;
    }

    public static Control CreateEasingSpanVisualizer(IKeyFrameAnimation animation, IKeyFrame keyframe)
    {
        Type type = keyframe.GetType().GetGenericArguments()[0];

        return (Control)Activator.CreateInstance(typeof(EasingFunctionSpanVisualizer<>).MakeGenericType(type), animation, keyframe)!;
    }

    public static Control CreateAnimationVisualizer(IAnimation animation)
    {
        if (animation is IAnimation<Media.Color> colorAnm)
        {
            return new ColorAnimationVisualizer(colorAnm);
        }

        Type type = animation.GetType().GetGenericArguments()[0];
        Type numberType = typeof(INumber<>);
        Type minMaxValueType = typeof(IMinMaxValue<>);
        Type binaryIntegerType = typeof(IBinaryInteger<>);
        Type floatingPointType = typeof(IFloatingPoint<>);

        if (IsAssignableToGenericType(type, numberType)
            && IsAssignableToGenericType(type, minMaxValueType))
        {
            if (IsAssignableToGenericType(type, binaryIntegerType))
            {
                return (Control)Activator.CreateInstance(typeof(IntegerAnimationVisualizer<>).MakeGenericType(type), animation)!;
            }
            else if (IsAssignableToGenericType(type, floatingPointType))
            {
                return (Control)Activator.CreateInstance(typeof(FloatingPointAnimationVisualizer<>).MakeGenericType(type), animation)!;
            }
        }

        return (Control)Activator.CreateInstance(typeof(EasingFunctionVisualizer<>).MakeGenericType(type), animation)!;
    }

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        foreach (Type it in givenType.GetInterfaces())
        {
            if (it.IsGenericType && it.GetGenericTypeDefinition() == genericType)
                return true;
        }

        if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
            return true;

        Type? baseType = givenType.BaseType;
        if (baseType == null)
            return false;

        return IsAssignableToGenericType(baseType, genericType);
    }
}
