namespace BeUtl.Animation.Easings;

public sealed class CubicEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.CubicEaseInOut(progress);
    }
}
