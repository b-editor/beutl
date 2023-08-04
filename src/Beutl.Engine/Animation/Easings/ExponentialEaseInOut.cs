namespace Beutl.Animation.Easings;

public sealed class ExponentialEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.ExponentialEaseInOut(progress);
    }
}
