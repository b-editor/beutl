namespace BeUtl.Animation.Animators;

public sealed class BoolAnimator : Animator<bool>
{
    public override bool Interpolate(float progress, bool oldValue, bool newValue)
    {
        if (progress >= 1d)
            return newValue;
        if (progress >= 0)
            return oldValue;
        return oldValue;
    }
}
