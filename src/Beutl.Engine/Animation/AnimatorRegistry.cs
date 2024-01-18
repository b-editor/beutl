using System.Numerics;

using Beutl.Animation.Animators;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Animation;

public static class AnimatorRegistry
{
    private static readonly List<(Func<Type, bool> Condition, Type Animator)> s_animators =
    [
        (type => typeof(bool).IsAssignableFrom(type), typeof(BoolAnimator)),
        (type => typeof(byte).IsAssignableFrom(type), typeof(ByteAnimator)),
        (type => typeof(Color).IsAssignableFrom(type), typeof(ColorAnimator)),
        (type => typeof(CornerRadius).IsAssignableFrom(type), typeof(CornerRadiusAnimator)),
        (type => typeof(decimal).IsAssignableFrom(type), typeof(DecimalAnimator)),
        (type => typeof(double).IsAssignableFrom(type), typeof(DoubleAnimator)),
        (type => typeof(float).IsAssignableFrom(type), typeof(FloatAnimator)),
        (type => typeof(short).IsAssignableFrom(type), typeof(Int16Animator)),
        (type => typeof(int).IsAssignableFrom(type), typeof(Int32Animator)),
        (type => typeof(long).IsAssignableFrom(type), typeof(Int64Animator)),
        (type => typeof(Matrix3x2).IsAssignableFrom(type), typeof(Matrix3x2Animator)),
        (type => typeof(Matrix4x4).IsAssignableFrom(type), typeof(Matrix4x4Animator)),
        (type => typeof(Matrix).IsAssignableFrom(type), typeof(MatrixAnimator)),
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
        (type => typeof(Graphics.Vector).IsAssignableFrom(type), typeof(VectorAnimator)),
    ];

    public static Animator<T> CreateAnimator<T>()
    {
        return (Activator.CreateInstance(GetAnimatorType(typeof(T))) as Animator<T>) ?? new _Animator<T>();
    }

    public static Type GetAnimatorType(Type type)
    {
        lock (s_animators)
        {
            foreach ((Func<Type, bool> condition, Type animator) in s_animators)
            {
                if (condition(type))
                {
                    return animator;
                }
            }

            return typeof(_Animator<>).MakeGenericType(type);
        }
    }

    public static void RegisterAnimator(Type animatorType, Func<Type, bool> condition)
    {
        lock (s_animators)
        {
            s_animators.Insert(0, (condition, animatorType));
        }
    }

    public static void RegisterAnimator<T, TAnimator>()
        where T : struct
        where TAnimator : Animator<T>
    {
        lock (s_animators)
        {
            s_animators.Insert(0, (type => typeof(T).IsAssignableFrom(type), typeof(TAnimator)));
        }
    }

    public static void RegisterAnimator<T, TAnimator>(Func<Type, bool> condition)
        where T : struct
        where TAnimator : Animator<T>
    {
        lock (s_animators)
        {
            s_animators.Insert(0, (condition, typeof(TAnimator)));
        }
    }

    private sealed class _Animator<T> : Animator<T>
    {
        public override T Interpolate(float progress, T oldValue, T newValue)
        {
            return newValue;
        }
    }
}
