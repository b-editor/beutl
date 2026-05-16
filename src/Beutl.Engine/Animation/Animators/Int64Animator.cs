namespace Beutl.Animation.Animators;

public sealed class Int64Animator : Animator<long>
{
    public override long Interpolate(float progress, long oldValue, long newValue)
    {
        // long.MaxValue 近傍では (double)oldValue が 2^53 ULP で丸まり、progress=0/1 でも
        // 端点を正確に返せない。境界進捗は double 経由を回避して oldValue/newValue を直接返す。
        if (progress is 0f) return oldValue;
        if (progress is 1f) return newValue;
        double delta = (double)newValue - oldValue;
        double v = oldValue + delta * progress;
        // (double)long.MaxValue は 2^63 に丸まり long に収まらないため Math.Clamp の
        // double 上限ではなく、long の正確な境界で飽和してから double→long に落とす。
        if (v >= long.MaxValue) return long.MaxValue;
        if (v <= long.MinValue) return long.MinValue;
        return (long)Math.Round(v, MidpointRounding.AwayFromZero);
    }
}
