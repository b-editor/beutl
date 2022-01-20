namespace BeUtl.Animation.Easings;

public sealed class QuadraticEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuadraticEaseOut(progress);
    }
}
