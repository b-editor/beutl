namespace Beutl.Animation.Animators;

public sealed class UInt64Animator : Animator<ulong>
{
    public override ulong Interpolate(float progress, ulong oldValue, ulong newValue)
    {
        // (double)oldValue は仮数 53bit に丸められ、ulong.MaxValue (2^64) 近傍では
        // 1 ULP ≒ 2^11 (約 2048) の誤差が乗る。progress=0/1 でも端点を正確に返せないため、
        // 境界進捗は double 経由を回避して oldValue/newValue を直接返す。
        if (progress is 0f) return oldValue;
        if (progress is 1f) return newValue;
        double delta = (double)newValue - (double)oldValue;
        double v = (double)oldValue + delta * progress;
        // (double)ulong.MaxValue は 2^64 に丸まり ulong に収まらないため、double のまま
        // ulong の境界と比較して飽和させ、確実に範囲内になった値だけを cast する。
        // 上流前提: progress は有限値で呼ばれること (Animator<T> / Easing 側で保証)。
        // NaN 入力は仕様上の保証対象外で、現状の cast 経路で 0 が返るが意図しないなら上流バグ。
        // 丸めは Math.Round 既定 (ToEven) のままにし、ByteAnimator/UInt16Animator など他の整数 Animator と挙動を一致させる。
        if (v >= ulong.MaxValue) return ulong.MaxValue;
        if (v <= 0d) return 0ul;
        return (ulong)Math.Round(v);
    }
}
