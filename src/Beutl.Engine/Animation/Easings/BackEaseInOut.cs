namespace Beutl.Animation.Easings;

public sealed class BackEaseInOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -0.195f;
        maximum = 1.195f;
        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.BackEaseInOut(progress);
    }
}
