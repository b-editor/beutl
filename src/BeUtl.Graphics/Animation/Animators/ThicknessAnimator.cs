using BeUtl.Graphics;

namespace BeUtl.Animation.Animators;

public sealed class ThicknessAnimator : Animator<Thickness>
{
    public override Thickness Interpolate(float progress, Thickness oldValue, Thickness newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}