namespace Beutl.Animation.Easings;

public sealed class QuarticEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuarticEaseIn(progress);
    }
}
