using BEditorNext.Animation.Easings;

namespace BEditorNext.Animation;

public interface IAnimation : IElement
{
    public Easing Easing { get; set; }

    public TimeSpan Duration { get; set; }

    public float Previous { get; }

    public float Next { get; }
}
