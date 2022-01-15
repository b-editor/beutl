namespace BeUtl.Animation.Easings;

public sealed class QuinticEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuinticEaseIn(progress);
    }
}
