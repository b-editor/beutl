namespace BeUtl.Animation.Easings;

public sealed class CircularEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.CircularEaseOut(progress);
    }
}
