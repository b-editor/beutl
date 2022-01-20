namespace BeUtl.Animation.Easings;

public sealed class SineEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.SineEaseInOut(progress);
    }
}
