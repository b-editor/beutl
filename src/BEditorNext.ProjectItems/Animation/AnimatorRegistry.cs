using System.Numerics;
using BEditorNext.Animation.Animators;
using BEditorNext.Graphics;

namespace BEditorNext.Animation;

public static class AnimatorRegistry
{
    private static readonly List<(Func<Type, bool> Condition, Type Animator)> Animators = new()
    {
        (type => typeof(bool).IsAssignableFrom(type), typeof(BoolAnimator)),
        (type => typeof(byte).IsAssignableFrom(type), typeof(ByteAnimator)),
        (type => typeof(Color).IsAssignableFrom(type), typeof(ColorAnimator)),
        (type => typeof(decimal).IsAssignableFrom(type), typeof(DecimalAnimator)),
        (type => typeof(double).IsAssignableFrom(type), typeof(DoubleAnimator)),
        (type => typeof(float).IsAssignableFrom(type), typeof(FloatAnimator)),
        (type => typeof(short).IsAssignableFrom(type), typeof(Int16Animator)),
        (type => typeof(int).IsAssignableFrom(type), typeof(Int32Animator)),
        (type => typeof(long).IsAssignableFrom(type), typeof(Int64Animator)),
        (type => typeof(Matrix3x2).IsAssignableFrom(type), typeof(Matrix3x2Animator)),
        (type => typeof(Matrix4x4).IsAssignableFrom(type), typeof(Matrix4x4Animator)),
        (type => typeof(PixelPoint).IsAssignableFrom(type), typeof(PixelPointAnimator)),
        (type => typeof(PixelRect).IsAssignableFrom(type), typeof(PixelRectAnimator)),
        (type => typeof(PixelSize).IsAssignableFrom(type), typeof(PixelSizeAnimator)),
        (type => typeof(Point).IsAssignableFrom(type), typeof(PointAnimator)),
        (type => typeof(Rect).IsAssignableFrom(type), typeof(RectAnimator)),
        (type => typeof(sbyte).IsAssignableFrom(type), typeof(SByteAnimator)),
        (type => typeof(Size).IsAssignableFrom(type), typeof(SizeAnimator)),
        (type => typeof(Thickness).IsAssignableFrom(type), typeof(ThicknessAnimator)),
        (type => typeof(ushort).IsAssignableFrom(type), typeof(UInt16Animator)),
        (type => typeof(uint).IsAssignableFrom(type), typeof(UInt32Animator)),
        (type => typeof(ulong).IsAssignableFrom(type), typeof(UInt64Animator)),
        (type => typeof(Vector2).IsAssignableFrom(type), typeof(Vector2Animator)),
        (type => typeof(Vector3).IsAssignableFrom(type), typeof(Vector3Animator)),
        (type => typeof(Vector4).IsAssignableFrom(type), typeof(Vector4Animator)),
    };

    public static Type GetAnimatorType(Type type)
    {
        foreach (var (condition, animator) in Animators)
        {
            if (condition(type))
            {
                return animator;
            }
        }

        throw new Exception($"Could not find an Animator that supports type {type.Name}.");
    }

    public static void RegisterAnimator(Type animatorType, Func<Type, bool> condition)
    {
        Animators.Insert(0, (condition, animatorType));
    }

    public static void RegisterAnimator<T, TAnimator>()
        where T : struct
        where TAnimator : Animator<T>
    {
        Animators.Insert(0, (type => typeof(T).IsAssignableFrom(type), typeof(TAnimator)));
    }

    public static void RegisterAnimator<T, TAnimator>(Func<Type, bool> condition)
        where T : struct
        where TAnimator : Animator<T>
    {
        Animators.Insert(0, (condition, typeof(TAnimator)));
    }
}
