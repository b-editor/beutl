namespace Beutl.Animation.Easings;

public sealed class HoldEasing : Easing
{
    public override float Ease(float progress)
    {
        return progress < 1f ? 0f : 1f;
    }
}
