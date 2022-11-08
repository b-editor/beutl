namespace Beutl.Animation.Easings;

public sealed class BounceEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.BounceEaseOut(progress);
    }
}
