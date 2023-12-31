using System.Globalization;
using System.Numerics;
using System.Text;

using static Beutl.Utilities.MathUtilities;

namespace Beutl;

public readonly partial struct Rational(long numerator, long denominator)
    : IEquatable<Rational>,
      IEqualityOperators<Rational, Rational, bool>,
      IMultiplyOperators<Rational, Rational, Rational>,
      IDivisionOperators<Rational, Rational, Rational>,
      IUnaryNegationOperators<Rational, Rational>,
      IAdditionOperators<Rational, Rational, Rational>,
      ISubtractionOperators<Rational, Rational, Rational>
{
    public Rational(long value)
      : this(value, 1)
    {
    }

    public long Numerator { get; } = numerator;

    public long Denominator { get; } = denominator;

    [Obsolete("Use Rational.IsNaN")]
    public bool IsIndeterminate => Denominator == 0 && Numerator == 0;

    public override bool Equals(object? obj)
    {
        return obj is Rational rational && Equals(rational);
    }

    public bool Equals(Rational other)
    {
        return (Numerator == other.Numerator && Denominator == other.Denominator)
            || (ToDouble() == other.ToDouble());
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Numerator, Denominator);
    }

    public override string ToString()
    {
        return ToString(CultureInfo.InvariantCulture);
    }

    public string ToString(IFormatProvider? provider)
    {
        if (IsNaN(this))
        {
            return "[ Indeterminate ]";
        }

        if (IsPositiveInfinity(this))
        {
            return "[ PositiveInfinity ]";
        }

        if (IsNegativeInfinity(this))
        {
            return "[ NegativeInfinity ]";
        }

        if (IsZero(this))
        {
            return "0";
        }

        if (IsInteger(this))
        {
            return Numerator.ToString(provider);
        }

        var sb = new StringBuilder();
        sb.Append(Numerator.ToString(provider));
        sb.Append('/');
        sb.Append(Denominator.ToString(provider));
        return sb.ToString();
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString(formatProvider);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (IsNaN(this))
        {
            return destination.TryWrite($"[ Indeterminate ]", out charsWritten);
        }

        if (IsPositiveInfinity(this))
        {
            return destination.TryWrite($"[ PositiveInfinity ]", out charsWritten);
        }

        if (IsNegativeInfinity(this))
        {
            return destination.TryWrite($"[ NegativeInfinity ]", out charsWritten);
        }

        if (IsZero(this))
        {
            return 0.TryFormat(destination, out charsWritten, format, provider);
        }

        if (IsInteger(this))
        {
            return Numerator.TryFormat(destination, out charsWritten, format, provider);
        }

        return destination.TryWrite(provider, $"{Numerator}/{Denominator}", out charsWritten);
    }

    public decimal ToDecimal()
    {
        return Numerator / (decimal)Denominator;
    }

    public double ToDouble()
    {
        return Numerator / (double)Denominator;
    }

    public float ToSingle()
    {
        return Numerator / (float)Denominator;
    }

    public static Rational FromDouble(double value, bool bestPrecision = true)
    {
        if (double.IsNaN(value))
        {
            return NaN;
        }

        if (double.IsPositiveInfinity(value))
        {
            return PositiveInfinity;
        }

        if (double.IsNegativeInfinity(value))
        {
            return NegativeInfinity;
        }

        long numerator = 1;
        long denominator = 1;

        double val = Math.Abs(value);
        double df = numerator / (double)denominator;
        double epsilon = bestPrecision ? double.Epsilon : .000001;

        while (Math.Abs(df - val) > epsilon)
        {
            if (df < val)
            {
                numerator++;
            }
            else
            {
                denominator++;
                numerator = (long)(val * denominator);
            }

            df = numerator / (double)denominator;
        }

        if (value < 0.0)
        {
            numerator *= -1;
        }

        return new Rational(numerator, denominator).Simplify();
    }

    public static Rational FromSingle(float value, bool bestPrecision = true)
    {
        if (float.IsNaN(value))
        {
            return NaN;
        }

        if (float.IsPositiveInfinity(value))
        {
            return PositiveInfinity;
        }

        if (float.IsNegativeInfinity(value))
        {
            return NegativeInfinity;
        }

        long numerator = 1;
        long denominator = 1;

        float val = MathF.Abs(value);
        float df = numerator / (float)denominator;
        float epsilon = bestPrecision ? float.Epsilon : .000001f;

        while (MathF.Abs(df - val) > epsilon)
        {
            if (df < val)
            {
                numerator++;
            }
            else
            {
                denominator++;
                numerator = (long)(val * denominator);
            }

            df = numerator / (float)denominator;
        }

        if (value < 0.0F)
        {
            numerator *= -1;
        }

        return new Rational(numerator, denominator).Simplify();
    }

    public static void Reduce(ref Rational left, ref Rational right)
    {
        long lcd = LeastCommonDenominator(left.Denominator, right.Denominator);

        long leftNum = left.Numerator * (lcd / left.Denominator);
        long rightNum = right.Numerator * (lcd / right.Denominator);

        left = new Rational(leftNum, lcd);
        right = new Rational(rightNum, lcd);
    }

    public Rational Simplify()
    {
        if (IsNaN(this) ||
            IsNegativeInfinity(this) ||
            IsPositiveInfinity(this) ||
            IsInteger(this) ||
            IsZero(this))
        {
            return this;
        }

        if (Numerator == 0)
        {
            return NaN;
        }

        if (Numerator == Denominator)
        {
            return new Rational(1, 1);
        }

        long gcd = GreatestCommonDivisor(Math.Abs(Numerator), Math.Abs(Denominator));

        if (gcd > 1)
        {
            return new Rational(Numerator / gcd, Denominator / gcd);
        }

        return this;
    }

    public static bool operator ==(Rational left, Rational right) => left.Equals(right);

    public static bool operator !=(Rational left, Rational right) => !(left == right);

    public static Rational operator *(Rational left, Rational right)
    {
        return new Rational(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    }

    public static Rational operator *(Rational left, int right)
    {
        return new Rational(left.Numerator * right, left.Denominator);
    }
    
    public static Rational operator *(Rational left, long right)
    {
        return new Rational(left.Numerator * right, left.Denominator);
    }

    public static Rational operator /(Rational left, Rational right)
    {
        if (right.Numerator == 0)
        {
            throw new DivideByZeroException();
        }

        return new Rational(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
    }

    public static Rational operator -(Rational value)
    {
        return new Rational(-value.Numerator, value.Denominator);
    }

    public static Rational operator +(Rational left, Rational right)
    {
        long lcd = LeastCommonDenominator(left.Denominator, right.Denominator);

        long leftNum = left.Numerator * (lcd / left.Denominator);
        long rightNum = right.Numerator * (lcd / right.Denominator);

        return new Rational(leftNum + rightNum, lcd);
    }

    public static Rational operator -(Rational left, Rational right)
    {
        long lcd = LeastCommonDenominator(left.Denominator, right.Denominator);

        long leftNum = left.Numerator * (lcd / left.Denominator);
        long rightNum = right.Numerator * (lcd / right.Denominator);

        return new Rational(leftNum - rightNum, lcd);
    }
}
