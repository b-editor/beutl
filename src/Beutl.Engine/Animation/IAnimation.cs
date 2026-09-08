using Beutl.Media;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

public interface IAnimation : INotifyEdited, ICoreSerializable, IHierarchical
{
    TimeSpan Duration { get; }

    bool UseGlobalClock { get; }

    Type ValueType { get; }
}

public interface IAnimation<T> : IAnimation
{
    IValidator<T>? Validator { get; set; }

    T? GetAnimatedValue(TimeSpan time);

    T? Interpolate(TimeSpan timeSpan);
}

/// <summary>
/// Provides a conservative output range for an animation with an ordered value type when one can be
/// computed without sampling.
/// </summary>
/// <typeparam name="T">The animation value type.</typeparam>
public interface IAnimationRange<T> : IAnimation<T>
    where T : IComparable<T>
{
    /// <summary>
    /// Attempts to return the minimum and maximum values produced by the animation.
    /// </summary>
    /// <param name="minimum">The minimum output value.</param>
    /// <param name="maximum">The maximum output value.</param>
    /// <returns><see langword="true"/> when the returned range contains every animation output.</returns>
    bool TryGetOutputRange(out T minimum, out T maximum);
}
