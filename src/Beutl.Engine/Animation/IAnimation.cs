using Beutl.Engine;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

public interface IAnimation : IAffectsRender, ICoreSerializable, IHierarchical
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

    IValidator<T>? Validator { get; set; }

    T? GetAnimatedValue(IClock clock);

    T? Interpolate(TimeSpan timeSpan);
}
