namespace BeUtl.Animation.Easings;

public sealed class BounceEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.BounceEaseInOut(progress);
    }
}
