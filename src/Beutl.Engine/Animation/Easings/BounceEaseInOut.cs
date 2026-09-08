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
        return Funcs.BounceEaseInOut(progress);
    }
}
