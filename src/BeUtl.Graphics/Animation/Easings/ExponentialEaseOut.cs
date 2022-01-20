namespace BeUtl.Animation.Easings;

public sealed class ExponentialEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.ExponentialEaseOut(progress);
    }
}
