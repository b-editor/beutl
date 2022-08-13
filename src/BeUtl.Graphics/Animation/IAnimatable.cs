namespace BeUtl.Animation;

public interface IAnimatable
{
    Animations Animations { get; }

    void ApplyAnimations(IClock clock);
}
