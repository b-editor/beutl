namespace BeUtl.Animation.Easings;

public sealed class QuinticEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuinticEaseInOut(progress);
    }
}
