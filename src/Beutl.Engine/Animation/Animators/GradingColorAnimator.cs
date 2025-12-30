using Beutl.Media;

namespace Beutl.Animation.Animators;

public sealed class GradingColorAnimator : Animator<GradingColor>
{
    public override GradingColor Interpolate(float progress, GradingColor oldValue, GradingColor newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
