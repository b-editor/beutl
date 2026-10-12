namespace Beutl.Animation.Easings;

public sealed class BounceEaseInOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -0.000001f;
        maximum = 1.000001f;
        return true;
    }

    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            return 0.5f * (1 - BounceEaseOut.Evaluate(1 - (progress * 2)));
        }
        else
        {
            return 0.5f * BounceEaseOut.Evaluate(progress * 2 - 1) + 0.5f;
        }
    }
}
