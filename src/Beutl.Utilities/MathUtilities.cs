namespace Beutl.Utilities;

public static class MathUtilities
{
    // smallest such that 1.0+DoubleEpsilon != 1.0
    public const double DoubleEpsilon = 2.2204460492503131e-016;

    public const float FloatEpsilon = 1.192092896e-07F;

    public static long GreatestCommonDivisor(long left, long right)
    {
        return right == 0 ? left : GreatestCommonDivisor(right, left % right);
    }

    public static long LeastCommonDenominator(long left, long right)
    {
        return left * right / GreatestCommonDivisor(left, right);
    }

    /// <summary>
    /// AreClose - Returns whether or not two doubles are "close".  That is, whether or
    /// not they are within epsilon of each other.
    /// </summary>
    /// <param name="value1"> The first double to compare. </param>
    /// <param name="value2"> The second double to compare. </param>
    public static bool AreClose(double value1, double value2)
    {
        //in case they are Infinities (then epsilon check does not work)
        if (value1 == value2) return true;
        double eps = (Math.Abs(value1) + Math.Abs(value2) + 10.0) * DoubleEpsilon;
        double delta = value1 - value2;
        return (-eps < delta) && (eps > delta);
    }

    /// <summary>
    /// AreClose - Returns whether or not two floats are "close".  That is, whether or
    /// not they are within epsilon of each other.
    /// </summary>
    /// <param name="value1"> The first float to compare. </param>
    /// <param name="value2"> The second float to compare. </param>
    public static bool AreClose(float value1, float value2)
    {
        //in case they are Infinities (then epsilon check does not work)
        if (value1 == value2) return true;
        float eps = (Math.Abs(value1) + Math.Abs(value2) + 10.0f) * FloatEpsilon;
        float delta = value1 - value2;
        return (-eps < delta) && (eps > delta);
    }

    /// <summary>
    /// LessThanOrClose - Returns whether or not the first double is less than or close to
    /// the second double.  That is, whether or not the first is strictly less than or within
    /// epsilon of the other number.
    /// </summary>
    /// <param name="value1"> The first double to compare. </param>
    /// <param name="value2"> The second double to compare. </param>
    public static bool LessThanOrClose(double value1, double value2)
    {
        return (value1 < value2) || AreClose(value1, value2);
    }

    /// <summary>
    /// LessThanOrClose - Returns whether or not the first float is less than or close to
    /// the second float.  That is, whether or not the first is strictly less than or within
    /// epsilon of the other number.
    /// </summary>
    /// <param name="value1"> The first float to compare. </param>
    /// <param name="value2"> The second float to compare. </param>
    public static bool LessThanOrClose(float value1, float value2)
    {
        return (value1 < value2) || AreClose(value1, value2);
    }

    /// <summary>
    /// IsZero - Returns whether or not the double is "close" to 0.  Same as AreClose(double, 0),
    /// but this is faster.
    /// </summary>
    /// <param name="value"> The double to compare to 0. </param>
    public static bool IsZero(double value)
    {
        return Math.Abs(value) < 10.0 * DoubleEpsilon;
    }

    /// <summary>
    /// IsZero - Returns whether or not the float is "close" to 0.  Same as AreClose(float, 0),
    /// but this is faster.
    /// </summary>
    /// <param name="value"> The float to compare to 0. </param>
    public static bool IsZero(float value)
    {
        return Math.Abs(value) < 10.0f * FloatEpsilon;
    }

    /// <summary>
    /// Converts an angle in radians to degrees.
    /// </summary>
    /// <param name="angle">The angle in radians.</param>
    /// <returns>The angle in degrees.</returns>
    public static float Rad2Deg(float angle)
    {
        return angle * (180f / MathF.PI);
    }

    /// <summary>
    /// Converts an angle in degrees to radians.
    /// </summary>
    /// <param name="angle">The angle in degrees.</param>
    /// <returns>The angle in radians.</returns>
    public static float Deg2Rad(float angle)
    {
        return angle * (MathF.PI / 180f);
    }

    /// <summary>
    /// Converts an angle in gradians to radians.
    /// </summary>
    /// <param name="angle">The angle in gradians.</param>
    /// <returns>The angle in radians.</returns>
    public static float Grad2Rad(float angle)
    {
        return angle * (MathF.PI / 200F);
    }

    /// <summary>
    /// Converts an angle in turns to radians.
    /// </summary>
    /// <param name="angle">The angle in turns.</param>
    /// <returns>The angle in radians.</returns>
    public static float Turn2Rad(float angle)
    {
        return angle * 2 * MathF.PI;
    }
}
