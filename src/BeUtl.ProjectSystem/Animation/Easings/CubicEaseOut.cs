namespace BeUtl.Animation.Easings;

public sealed class CubicEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.CubicEaseOut(progress);
    }
}
