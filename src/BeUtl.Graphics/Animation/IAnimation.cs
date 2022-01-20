using BeUtl.Animation.Easings;

namespace BeUtl.Animation;

public interface IAnimation : ICoreObject, ILogicalElement
{
    public Easing Easing { get; set; }

    public TimeSpan Duration { get; set; }

    public Animator Animator { get; }

    public object Previous { get; set; }

    public object Next { get; set; }
}
