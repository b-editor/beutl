namespace Beutl.Animation.Animators;

public sealed class Int64Animator : Animator<long>
{
    public override long Interpolate(float progress, long oldValue, long newValue)
    {
        // (double)oldValue は仮数 53bit に丸められ、long.MaxValue (2^63) 近傍では
        // 1 ULP ≒ 2^10 (約 1024) の誤差が乗る。progress=0/1 でも端点を正確に返せないため、
        // 境界進捗は double 経由を回避して oldValue/newValue を直接返す。
        if (progress is 0f) return oldValue;
        if (progress is 1f) return newValue;
        double delta = (double)newValue - oldValue;
        double v = oldValue + delta * progress;
        // Math.Clamp(v, long.MinValue, long.MaxValue) を使っても、(double)long.MaxValue は
        // 2^63 に丸まる (double で表現可能な最近接値)。これは long.MaxValue + 1 に相当し
        // long には収まらないため、(long) キャストで未定義に近い値になる。よって double の
        // まま long の境界と比較して飽和させ、確実に範囲内になった値だけを cast する。
        if (v >= long.MaxValue) return long.MaxValue;
        if (v <= long.MinValue) return long.MinValue;
        return (long)Math.Round(v, MidpointRounding.AwayFromZero);
    }
}
