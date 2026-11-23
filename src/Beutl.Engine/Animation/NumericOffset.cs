using System.Numerics;

namespace Beutl.Animation;

public static class NumericOffset
{
    public static T Add<T>(T value, T from, T to) where T : INumber<T>
    {
        try
        {
            return checked(value + checked(to - from));
        }
        catch (OverflowException)
        {
            // Widen integer deltas so unsigned components can move down and saturate at their limits.
            if (T.IsInteger(value) && T.IsInteger(from) && T.IsInteger(to))
                return T.CreateSaturating(BigInteger.CreateChecked(value) + BigInteger.CreateChecked(to) - BigInteger.CreateChecked(from));
            return T.CreateSaturating(double.CreateChecked(value) + (double.CreateChecked(to) - double.CreateChecked(from)));
        }
    }

    internal static T AddScaled<T>(T value, T from, T to, double scale) where T : INumber<T>
    {
        if (scale == 1)
            return Add(value, from, to);

        // Keep integral cycle offsets exact even when the values exceed double's integer precision.
        if (double.IsInteger(scale) && T.IsInteger(value) && T.IsInteger(from) && T.IsInteger(to))
        {
            return T.CreateSaturating(BigInteger.CreateChecked(value)
                + (BigInteger.CreateChecked(to) - BigInteger.CreateChecked(from)) * new BigInteger(scale));
        }

        // An integer type (one that truncates 0.5 to zero) keeps its value exact and rounds only the scaled delta.
        if (T.IsZero(T.CreateTruncating(0.5)))
        {
            double offset = Math.Round(double.CreateChecked(BigInteger.CreateChecked(to) - BigInteger.CreateChecked(from)) * scale);
            return double.IsFinite(offset)
                ? T.CreateSaturating(BigInteger.CreateChecked(value) + new BigInteger(offset))
                : T.CreateSaturating(offset);
        }

        return T.CreateSaturating(double.CreateChecked(value)
            + (double.CreateChecked(to) - double.CreateChecked(from)) * scale);
    }
}
