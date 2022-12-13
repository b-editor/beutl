
using Beutl.Animation.Easings;
using Beutl.Media;

namespace Beutl.Animation;

public interface IAnimationSpan : ICoreObject
{
    Easing Easing { get; set; }

    TimeSpan Duration { get; set; }

    object Previous { get; set; }

    object Next { get; set; }

    Animator Animator { get; }

    event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    object Interpolate(float progress);
}
