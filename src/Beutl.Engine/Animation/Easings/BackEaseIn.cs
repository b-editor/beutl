namespace Beutl.Animation.Easings;

public sealed class BackEaseIn : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        // p^3 - p*sin(pi*p), with p in [0,1]. Since p^3 - p is minimized at
        // -2/(3*sqrt(3)) ~= -0.3849 and sin(pi*p) <= 1, [-0.39, 1] is conservative.
        minimum = -0.39f;
        maximum = 1;
        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.BackEaseIn(progress);
    }
}
