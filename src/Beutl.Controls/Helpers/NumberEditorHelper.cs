using System.Numerics;

namespace Beutl;

public static class NumberEditorHelper
{
    public static TValue AddPreservingScale<TValue>(TValue original, TValue delta)
        where TValue : INumber<TValue>
    {
        // original のスケールに合わせて delta を調整
        var originalDecimal = decimal.CreateTruncating(original);
        var deltaDecimal = decimal.CreateTruncating(delta);
        int scale = Math.Max(GetScale(originalDecimal), GetScale(deltaDecimal));
        decimal result = originalDecimal + deltaDecimal;

        // 同じスケールに正規化
        return TValue.CreateTruncating(decimal.Round(result, scale));
    }

    public static int GetScale(decimal value)
    {
        // decimal のビット表現からスケールを取得
        int[] bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0xFF;
    }
}
