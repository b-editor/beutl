namespace BeUtl.Animation.Easings;

public sealed class ExponentialEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.ExponentialEaseIn(progress);
    }
}
