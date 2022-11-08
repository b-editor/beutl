namespace Beutl.Animation.Easings;

public sealed class BackEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.BackEaseInOut(progress);
    }
}
