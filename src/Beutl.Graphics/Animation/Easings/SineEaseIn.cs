namespace Beutl.Animation.Easings;

public sealed class SineEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.SineEaseIn(progress);
    }
}
