namespace Beutl.Animation.Easings;

public sealed class QuarticEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuarticEaseOut(progress);
    }
}
