namespace Beutl.Animation;

public interface IAnimationSpan<T> : IAnimationSpan
{
    new T Previous { get; set; }

    new T Next { get; set; }

    new Animator<T> Animator { get; }

    object IAnimationSpan.Previous
    {
        get => Previous!;
        set => Previous = (T)value;
    }
    
    object IAnimationSpan.Next
    {
        get => Next!;
        set => Next = (T)value;
    }

    Animator IAnimationSpan.Animator => Animator;

    new T Interpolate(float progress);

    object IAnimationSpan.Interpolate(float progress) => Interpolate(progress)!;
}
