using Beutl.Graphics;

namespace Beutl.Animation.Animators;

public sealed class RelativeRectAnimator : Animator<RelativeRect>
{
    private static readonly RectAnimator s_rectAnimator = new();

    public override RelativeRect Interpolate(float progress, RelativeRect oldValue, RelativeRect newValue)
    {
        if (oldValue.Unit != newValue.Unit)
        {
            return progress >= 0.5 ? newValue : oldValue;
        }

        return new RelativeRect(s_rectAnimator.Interpolate(progress, oldValue.Rect, newValue.Rect), oldValue.Unit);
    }
}
