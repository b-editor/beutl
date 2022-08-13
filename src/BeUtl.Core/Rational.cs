using System.Globalization;
using System.Text;

namespace BeUtl.Media;

public readonly struct Rational : IEquatable<Rational>
{
    public Rational(int value)
      : this(value, 1)
    {
    }

    public Rational(int numerator, int denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public int Numerator { get; }

    public int Denominator { get; }

    public bool IsIndeterminate => Denominator == 0 && Numerator == 0;

    public bool IsInteger => Denominator == 1;

    public bool IsNegativeInfinity => Denominator == 0 && Numerator == -1;

    public bool IsPositiveInfinity => Denominator == 0 && Numerator == 1;

    public bool IsZero => Denominator == 1 && Numerator == 0;

    public override bool Equals(object? obj)
    {
        return obj is Rational rational && Equals(rational);
    }

    public bool Equals(Rational other)
    {
        return Numerator == other.Numerator && Denominator == other.Denominator;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Numerator, Denominator);
    }

    public override string ToString()
    {
        return ToString(CultureInfo.InvariantCulture);
    }

    public string ToString(IFormatProvider provider)
    {
        if (IsIndeterminate)
        {
            return "[ Indeterminate ]";
        }

        if (IsPositiveInfinity)
        {
            return "[ PositiveInfinity ]";
        }

        if (IsNegativeInfinity)
        {
            return "[ NegativeInfinity ]";
        }

        if (IsZero)
        {
            return "0";
        }

        if (IsInteger)
        {
            return Numerator.ToString(provider);
        }

        var sb = new StringBuilder();
        sb.Append(Numerator.ToString(provider));
        sb.Append('/');
        sb.Append(Denominator.ToString(provider));
        return sb.ToString();
    }

    public double ToDouble()
    {
        return Numerator / (double)Denominator;
    }

    public float ToSingle()
    {
        return Numerator / (float)Denominator;
    }

    public static Rational FromDouble(double value, bool bestPrecision)
    {
        if (double.IsNaN(value))
        {
            return new Rational(0, 0);
        }

        if (double.IsPositiveInfinity(value))
        {
            return new Rational(1, 0);
        }

        if (double.IsNegativeInfinity(value))
        {
            return new Rational(-1, 0);
        }

        int numerator = 1;
        int denominator = 1;

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
                numerator = (int)(val * denominator);
            }

            df = numerator / (double)denominator;
        }

        if (value < 0.0)
        {
            numerator *= -1;
        }

        return new Rational(numerator, denominator).Simplify();
    }

    public static Rational FromSingle(float value, bool bestPrecision)
    {
        if (float.IsNaN(value))
        {
            return new Rational(0, 0);
        }

        if (float.IsPositiveInfinity(value))
        {
            return new Rational(1, 0);
        }

        if (float.IsNegativeInfinity(value))
        {
            return new Rational(-1, 0);
        }

        int numerator = 1;
        int denominator = 1;

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
                numerator = (int)(val * denominator);
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
        int lcd = LeastCommonDenominator(left.Denominator, right.Denominator);

        int leftNum = left.Numerator * (lcd / left.Denominator);
        int rightNum = right.Numerator * (lcd / right.Denominator);

        left = new Rational(leftNum, lcd);
        right = new Rational(rightNum, lcd);
    }

    // Todo: MathUtillitiesに移動
    private static int GreatestCommonDivisor(int left, int right)
    {
        return right == 0 ? left : GreatestCommonDivisor(right, left % right);
    }

    // Todo: MathUtillitiesに移動
    private static int LeastCommonDenominator(int left, int right)
    {
        return left * right / GreatestCommonDivisor(left, right);
    }

    public Rational Simplify()
    {
        if (IsIndeterminate ||
            IsNegativeInfinity ||
            IsPositiveInfinity ||
            IsInteger ||
            IsZero)
        {
            return this;
        }

        if (Numerator == 0)
        {
            return new Rational(0, 0);
        }

        if (Numerator == Denominator)
        {
            return new Rational(1, 1);
        }

        int gcd = GreatestCommonDivisor(Math.Abs(Numerator), Math.Abs(Denominator));

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
        int lcd = LeastCommonDenominator(left.Denominator, right.Denominator);

        int leftNum = left.Numerator * (lcd / left.Denominator);
        int rightNum = right.Numerator * (lcd / right.Denominator);

        return new Rational(leftNum + rightNum, lcd);
    }

    public static Rational operator -(Rational left, Rational right)
    {
        int lcd = LeastCommonDenominator(left.Denominator, right.Denominator);

        int leftNum = left.Numerator * (lcd / left.Denominator);
        int rightNum = right.Numerator * (lcd / right.Denominator);

        return new Rational(leftNum - rightNum, lcd);
    }
}
