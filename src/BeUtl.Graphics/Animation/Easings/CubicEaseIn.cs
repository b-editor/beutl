namespace BeUtl.Animation.Easings;

public sealed class CubicEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.CubicEaseIn(progress);
    }
}
