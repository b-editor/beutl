using System.Numerics;

using Avalonia.Input;

namespace Beutl;

public static class NumberEditorHelper
{
    public static double GetScrubModifierCoefficient(KeyModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        bool fine = modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Meta);
        if (shift && fine)
        {
            return 1.0;
        }
        if (shift)
        {
            return 10.0;
        }
        if (fine)
        {
            return 0.1;
        }
        return 1.0;
    }

    public static double ApplyScrubModifier(double move, KeyModifiers modifiers)
    {
        return move * GetScrubModifierCoefficient(modifiers);
    }

    // 整数型 TValue では小数移動量が CreateTruncating で 0 に切り捨てられ、
    // fine modifier (係数 0.1) ではドラッグがほぼ反応しなくなる。
    // ここでフレーム間の小数残差を accumulator に持ち越し、整数化できた分のみ返す。
    public static TValue ConsumeScrubAccumulator<TValue>(ref double accumulator, double scaledMove)
        where TValue : INumber<TValue>
    {
        accumulator += scaledMove;
        TValue truncated = TValue.CreateTruncating(accumulator);
        accumulator -= double.CreateTruncating(truncated);
        return truncated;
    }

    public static TValue AddPreservingScale<TValue>(TValue original, TValue delta)
        where TValue : INumber<TValue>
    {
        try
        {
            // original のスケールに合わせて delta を調整
            var originalDecimal = decimal.CreateTruncating(original);
            var deltaDecimal = decimal.CreateTruncating(delta);
            int scale = Math.Max(GetScale(originalDecimal), GetScale(deltaDecimal));
            decimal result = originalDecimal + deltaDecimal;

            // 同じスケールに正規化
            return TValue.CreateTruncating(decimal.Round(result, scale));
        }
        catch (OverflowException)
        {
            // decimal に収まらない場合はスケール保持を諦めて単純加算にフォールバック
            return original + delta;
        }
    }

    public static int GetScale(decimal value)
    {
        // decimal のビット表現からスケールを取得
        int[] bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0xFF;
    }
}
