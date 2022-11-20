using System.Numerics;

using Avalonia.Controls;

namespace Beutl.Views.AnimationVisualizer;

public static class AnimationVisualizerExtensions
{
    public static Control CreateAnimationSpanVisualizer(Animation.IAnimation animation, Animation.IAnimationSpan animationSpan)
    {
        if (animation is Animation.Animation<Media.Color> colorAnm
            && animationSpan is Animation.AnimationSpan<Media.Color> colorAnmSpan)
        {
            return new ColorAnimationSpanVisualizer(colorAnm, colorAnmSpan);
        }

        Type type = animationSpan.GetType().GetGenericArguments()[0];
        Type numberType = typeof(INumber<>);
        Type minMaxValueType = typeof(IMinMaxValue<>);
        Type binaryIntegerType = typeof(IBinaryInteger<>);
        Type floatingPointType = typeof(IFloatingPoint<>);

        if (IsAssignableToGenericType(type, numberType)
            && IsAssignableToGenericType(type, minMaxValueType))
        {
            if (IsAssignableToGenericType(type, binaryIntegerType))
            {
                return (Control)Activator.CreateInstance(typeof(IntegerAnimationSpanVisualizer<>).MakeGenericType(type), animation, animationSpan)!;
            }
            else if (IsAssignableToGenericType(type, floatingPointType))
            {
                return (Control)Activator.CreateInstance(typeof(FloatingPointAnimationSpanVisualizer<>).MakeGenericType(type), animation, animationSpan)!;
            }
        }

        return (Control)Activator.CreateInstance(typeof(EasingFunctionSpanVisualizer<>).MakeGenericType(type), animation, animationSpan)!;
    }

    public static Control CreateEasingSpanVisualizer(Animation.IAnimation animation, Animation.IAnimationSpan animationSpan)
    {
        Type type = animationSpan.GetType().GetGenericArguments()[0];

        return (Control)Activator.CreateInstance(typeof(EasingFunctionSpanVisualizer<>).MakeGenericType(type), animation, animationSpan)!;
    }

    public static Control CreateAnimationVisualizer(Animation.IAnimation animation)
    {
        if (animation is Animation.Animation<Media.Color> colorAnm)
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
