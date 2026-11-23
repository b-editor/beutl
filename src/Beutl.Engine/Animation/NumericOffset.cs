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

        // An integer type (one that truncates 0.5 to zero) keeps its value exact and rounds the sum half to even,
        // as the integer animators' Math.Round does.
        if (T.IsZero(T.CreateTruncating(0.5)))
        {
            double offset = double.CreateChecked(BigInteger.CreateChecked(to) - BigInteger.CreateChecked(from)) * scale;
            if (!double.IsFinite(offset))
                return T.CreateSaturating(offset);

            double whole = Math.Floor(offset);
            double fraction = offset - whole;
            BigInteger sum = BigInteger.CreateChecked(value) + new BigInteger(whole);
            if (fraction > 0.5 || (fraction == 0.5 && !sum.IsEven))
                sum += 1;
            return T.CreateSaturating(sum);
        }

        // Other types scale in their own arithmetic, keeping a decimal's digits, and saturate on overflow.
        try
        {
            return checked(value + checked(to - from) * T.CreateChecked(scale));
        }
        catch (OverflowException)
        {
            return T.CreateSaturating(double.CreateChecked(value)
                + (double.CreateChecked(to) - double.CreateChecked(from)) * scale);
        }
    }
}
