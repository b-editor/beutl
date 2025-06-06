using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;

using Beutl.Animation.Animators;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Animation;

/// <summary>
/// Optimized animator registry with caching and pre-computed lookups.
/// Provides O(1) lookup performance for known types and thread-safe registration.
/// </summary>
public static class AnimatorRegistry
{
    // Pre-computed dictionary for O(1) lookup of known animators
    private static readonly Dictionary<Type, Type> s_knownAnimators = new()
    {
        [typeof(bool)] = typeof(BoolAnimator),
        [typeof(byte)] = typeof(ByteAnimator),
        [typeof(sbyte)] = typeof(SByteAnimator),
        [typeof(short)] = typeof(Int16Animator),
        [typeof(ushort)] = typeof(UInt16Animator),
        [typeof(int)] = typeof(Int32Animator),
        [typeof(uint)] = typeof(UInt32Animator),
        [typeof(long)] = typeof(Int64Animator),
        [typeof(ulong)] = typeof(UInt64Animator),
        [typeof(float)] = typeof(FloatAnimator),
        [typeof(double)] = typeof(DoubleAnimator),
        [typeof(decimal)] = typeof(DecimalAnimator),
        [typeof(Color)] = typeof(ColorAnimator),
        [typeof(CornerRadius)] = typeof(CornerRadiusAnimator),
        [typeof(Matrix3x2)] = typeof(Matrix3x2Animator),
        [typeof(Matrix4x4)] = typeof(Matrix4x4Animator),
        [typeof(Matrix)] = typeof(MatrixAnimator),
        [typeof(PixelPoint)] = typeof(PixelPointAnimator),
        [typeof(PixelRect)] = typeof(PixelRectAnimator),
        [typeof(PixelSize)] = typeof(PixelSizeAnimator),
        [typeof(Point)] = typeof(PointAnimator),
        [typeof(Rect)] = typeof(RectAnimator),
        [typeof(Size)] = typeof(SizeAnimator),
        [typeof(Thickness)] = typeof(ThicknessAnimator),
        [typeof(Vector2)] = typeof(Vector2Animator),
        [typeof(Vector3)] = typeof(Vector3Animator),
        [typeof(Vector4)] = typeof(Vector4Animator),
        [typeof(Graphics.Vector)] = typeof(VectorAnimator),
        [typeof(RelativePoint)] = typeof(RelativePointAnimator),
        [typeof(RelativeRect)] = typeof(RelativeRectAnimator),
    };

    // Lock-free collection for custom animators
    private static volatile ImmutableList<(Func<Type, bool> Condition, Type Animator)> s_customAnimators = [];

    // Cache for resolved animator types - provides O(1) lookup after first resolution
    private static readonly ConcurrentDictionary<Type, Type> s_animatorCache = new();

    /// <summary>
    /// Creates an animator instance for the specified type.
    /// Uses caching for optimal performance.
    /// </summary>
    public static Animator<T> CreateAnimator<T>()
    {
        Type animatorType = GetAnimatorType(typeof(T));
        return (Activator.CreateInstance(animatorType) as Animator<T>) ?? new _Animator<T>();
    }

    /// <summary>
    /// Gets the animator type for the specified type.
    /// Uses pre-computed dictionary and caching for O(1) performance.
    /// </summary>
    public static Type GetAnimatorType(Type type)
    {
        return s_animatorCache.GetOrAdd(type, ComputeAnimatorType);
    }

    /// <summary>
    /// Registers a custom animator with thread-safe lock-free updates.
    /// </summary>
    public static void RegisterAnimator(Type animatorType, Func<Type, bool> condition)
    {
        ImmutableInterlocked.Update(ref s_customAnimators, 
            list => list.Insert(0, (condition, animatorType)));
        
        // Clear cache since new animator might affect existing lookups
        s_animatorCache.Clear();
    }

    /// <summary>
    /// Registers a custom animator for a specific type.
    /// </summary>
    public static void RegisterAnimator<T, TAnimator>()
        where TAnimator : Animator<T>, new()
    {
        RegisterAnimator(typeof(TAnimator), type => typeof(T).IsAssignableFrom(type));
    }

    /// <summary>
    /// Registers a custom animator with a custom condition.
    /// </summary>
    public static void RegisterAnimator<T, TAnimator>(Func<Type, bool> condition)
        where TAnimator : Animator<T>, new()
    {
        RegisterAnimator(typeof(TAnimator), condition);
    }

    /// <summary>
    /// Computes the animator type for a given type.
    /// First checks known animators (O(1)), then custom animators (O(n)), 
    /// finally falls back to generic numeric or default animator.
    /// </summary>
    private static Type ComputeAnimatorType(Type type)
    {
        // Fast path: Check pre-computed known animators
        if (s_knownAnimators.TryGetValue(type, out Type? knownAnimator))
        {
            return knownAnimator;
        }

        // Check custom registered animators
        var customAnimators = s_customAnimators; // Atomic read
        foreach ((Func<Type, bool> condition, Type animator) in customAnimators)
        {
            if (condition(type))
            {
                return animator;
            }
        }

        // For unknown numeric types, try to use NumericAnimator<T>
        if (type.IsValueType && IsNumericType(type))
        {
            return typeof(NumericAnimator<>).MakeGenericType(type);
        }

        // Default fallback
        return typeof(_Animator<>).MakeGenericType(type);
    }

    /// <summary>
    /// Checks if a type implements INumber&lt;T&gt; and can use NumericAnimator.
    /// </summary>
    private static bool IsNumericType(Type type)
    {
        if (!type.IsValueType) return false;

        var numberInterface = typeof(IFloatingPointIeee754<>).MakeGenericType(type);
        return numberInterface.IsAssignableFrom(type);
    }

    /// <summary>
    /// Default animator that returns the new value without interpolation.
    /// </summary>
    internal sealed class _Animator<T> : Animator<T>
    {
        public override T Interpolate(float progress, T oldValue, T newValue)
        {
            return newValue;
        }
    }
}
