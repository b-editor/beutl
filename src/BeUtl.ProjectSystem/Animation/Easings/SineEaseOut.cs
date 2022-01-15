namespace BeUtl.Animation.Easings;

public sealed class SineEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.SineEaseOut(progress);
    }
}
