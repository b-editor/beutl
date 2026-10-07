namespace Beutl.Animation.Animators;

public sealed class BoolAnimator : Animator<bool>
{
    public override bool Interpolate(float progress, bool oldValue, bool newValue)
    {
        if (progress >= 1d)
            return newValue;
        return oldValue;
    }
}
