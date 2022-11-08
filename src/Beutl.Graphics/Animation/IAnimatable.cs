namespace Beutl.Animation;

public interface IAnimatable
{
    Animations Animations { get; }

    void ApplyAnimations(IClock clock);
}
