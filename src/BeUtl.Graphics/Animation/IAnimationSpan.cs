
using BeUtl.Animation.Easings;

namespace BeUtl.Animation;

public interface IAnimationSpan : ICoreObject
{
    Easing Easing { get; set; }

    TimeSpan Duration { get; set; }

    object Previous { get; set; }

    object Next { get; set; }

    event EventHandler? Invalidated;

    object Interpolate(float progress);
}
