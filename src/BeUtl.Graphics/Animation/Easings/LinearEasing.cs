namespace Beutl.Animation.Easings;

public sealed class LinearEasing : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.LinearEasing(progress);
    }
}
