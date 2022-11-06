namespace Beutl.Animation.Animators;

public sealed class FloatAnimator : Animator<float>
{
    public override float Interpolate(float progress, float oldValue, float newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
