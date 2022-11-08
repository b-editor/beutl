namespace Beutl.Animation.Easings;

public sealed class BounceEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.BounceEaseIn(progress);
    }
}
