using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Animation;

public interface IAnimation : IJsonSerializable, IAffectsRender, ICoreSerializable
{
    CoreProperty Property { get; }

    TimeSpan Duration { get; }

    bool UseGlobalClock { get; }

    void ApplyAnimation(Animatable target, IClock clock);
}

public interface IAnimation<T> : IAnimation
{
    new CoreProperty<T> Property { get; }

    CoreProperty IAnimation.Property => Property;

    T? GetAnimatedValue(IClock clock);

    T? Interpolate(TimeSpan timeSpan);
}
