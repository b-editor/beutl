using Beutl.Engine;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

public interface IAnimation : IAffectsRender, ICoreSerializable, IHierarchical
{
    TimeSpan Duration { get; }

    bool UseGlobalClock { get; }
}

public interface IAnimation<T> : IAnimation
{
    IValidator<T>? Validator { get; set; }

    T? GetAnimatedValue(IClock clock);

    T? Interpolate(TimeSpan timeSpan);
}
