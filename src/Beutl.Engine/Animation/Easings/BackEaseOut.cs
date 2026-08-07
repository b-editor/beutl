namespace Beutl.Animation.Easings;

public sealed class BackEaseOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = 0;
        maximum = 1.39f;
        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.BackEaseOut(progress);
    }
}
