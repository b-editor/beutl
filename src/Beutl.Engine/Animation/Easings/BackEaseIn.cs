namespace Beutl.Animation.Easings;

public sealed class BackEaseIn : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        // p^3 - p*sin(pi*p), with p in [0,1]. Since p^3 - p is minimized at
        // -2/(3*sqrt(3)) ~= -0.3849 and sin(pi*p) <= 1, the widened endpoint keeps
        // the single-precision evaluation at p=1 inside the advertised range.
        minimum = -0.39f;
        maximum = 1.000001f;
        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.BackEaseIn(progress);
    }
}
