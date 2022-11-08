using Beutl.Graphics;

namespace Beutl.Animation.Animators;

public sealed class ThicknessAnimator : Animator<Thickness>
{
    public override Thickness Interpolate(float progress, Thickness oldValue, Thickness newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}