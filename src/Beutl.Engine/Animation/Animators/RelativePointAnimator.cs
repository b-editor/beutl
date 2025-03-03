using Beutl.Graphics;

namespace Beutl.Animation.Animators;

public sealed class RelativePointAnimator : Animator<RelativePoint>
{
    private static readonly PointAnimator s_pointAnimator = new();

    public override RelativePoint Interpolate(float progress, RelativePoint oldValue, RelativePoint newValue)
    {
        if (oldValue.Unit != newValue.Unit)
        {
            return progress >= 0.5 ? newValue : oldValue;
        }

        return new RelativePoint(s_pointAnimator.Interpolate(progress, oldValue.Point, newValue.Point), oldValue.Unit);
    }
}
