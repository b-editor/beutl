namespace BeUtl.Animation.Easings;

public sealed class BackEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.BackEaseOut(progress);
    }
}
