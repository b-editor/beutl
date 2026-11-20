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
}
