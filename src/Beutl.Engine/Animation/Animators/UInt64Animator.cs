namespace Beutl.Animation.Animators;

public sealed class UInt64Animator : Animator<ulong>
{
    public override ulong Interpolate(float progress, ulong oldValue, ulong newValue)
    {
        // ulong.MaxValue 近傍では (double)oldValue が ULP で丸まり、progress=0/1 でも
        // 端点を正確に返せない。境界進捗は double 経由を回避して oldValue/newValue を直接返す。
        if (progress is 0f) return oldValue;
        if (progress is 1f) return newValue;
        double delta = (double)newValue - (double)oldValue;
        double v = (double)oldValue + delta * progress;
        // (double)ulong.MaxValue は 2^64 に丸まり ulong に収まらないため double 比較で
        // 飽和してから ulong に落とす。NaN は最終 cast 経路で 0 になる従来挙動を維持。
        if (v >= ulong.MaxValue) return ulong.MaxValue;
        if (v <= 0d) return 0ul;
        return (ulong)Math.Round(v, MidpointRounding.AwayFromZero);
    }
}
