namespace BeUtl.Animation.Easings;

public sealed class QuinticEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuinticEaseOut(progress);
    }
}
