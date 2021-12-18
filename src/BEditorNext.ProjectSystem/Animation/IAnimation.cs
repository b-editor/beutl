using BEditorNext.Animation.Easings;

namespace BEditorNext.Animation;

public interface IAnimation : IElement
{
    public Easing Easing { get; set; }

    public TimeSpan Duration { get; set; }

    public Animator Animator { get; }

    public object Previous { get; set; }

    public object Next { get; set; }
}
