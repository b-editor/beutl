using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

using Beutl.JsonConverters;

namespace Beutl;

[JsonConverter(typeof(RationalJsonConverter))]
public readonly partial struct Rational : INumber<Rational>, IMinMaxValue<Rational>
{
    public static Rational NaN => new(0, 0);

    public static Rational PositiveInfinity => new(1, 0);

    public static Rational NegativeInfinity => new(-1, 0);

    public static Rational MaxValue => new(long.MaxValue);

    public static Rational MinValue => new(long.MinValue);

    public static Rational One => new(1);

    public static int Radix => 2;

    public static Rational Zero => new(0);

    public static Rational AdditiveIdentity => new(0);

    public static Rational MultiplicativeIdentity => new(1);

    public static Rational Abs(Rational value)
    {
        long num = Math.Abs(value.Numerator);
        long den = Math.Abs(value.Denominator);
        return new Rational(num, den);
    }

    static bool INumberBase<Rational>.IsCanonical(Rational value) => true;

    static bool INumberBase<Rational>.IsComplexNumber(Rational value) => false;

    public static bool IsEvenInteger(Rational value)
    {
        return IsInteger(value) && (Abs(value % 2) == Zero);
    }

    public static bool IsFinite(Rational value)
    {
        return !(IsPositiveInfinity(value) || IsNegativeInfinity(value));
    }

    static bool INumberBase<Rational>.IsImaginaryNumber(Rational value) => false;

    public static bool IsInfinity(Rational value)
    {
        return IsPositiveInfinity(value) || IsNegativeInfinity(value);
    }

    public static bool IsNaN(Rational value)
    {
        return value.Denominator == 0 && value.Numerator == 0;
    }

    public static bool IsNegative(Rational value)
    {
        return long.IsNegative(value.Numerator) ^ long.IsNegative(value.Denominator);
    }

    public static bool IsNormal(Rational value)
    {
        return !(IsInfinity(value) || IsZero(value) || IsNaN(value));
    }

    public static bool IsOddInteger(Rational value) => IsInteger(value) && (Abs(value % 2) == One);

    public static bool IsPositive(Rational value)
    {
        return long.IsPositive(value.Numerator) ^ long.IsPositive(value.Denominator);
    }

    public static bool IsRealNumber(Rational value)
    {
        return !IsNaN(value);
    }

    public static bool IsSubnormal(Rational value)
    {
        return IsInfinity(value) || IsZero(value) || IsNaN(value);
    }

    public static Rational MaxMagnitude(Rational x, Rational y)
    {
        if (IsNaN(x) || IsNaN(y))
            return NaN;

        Rational ax = Abs(x);
        Rational ay = Abs(y);

        return ax > ay ? x : y;
    }

    public static Rational MaxMagnitudeNumber(Rational x, Rational y)
    {
        Rational ax = Abs(x);
        Rational ay = Abs(y);

        if ((ax > ay) || IsNaN(ay))
        {
            return x;
        }

        if (ax == ay)
        {
            return IsNegative(x) ? y : x;
        }

        return y;
    }

    public static Rational MinMagnitude(Rational x, Rational y)
    {
        if (IsNaN(x) || IsNaN(y))
            return NaN;

        Rational ax = Abs(x);
        Rational ay = Abs(y);

        return ax < ay ? x : y;
    }

    public static Rational MinMagnitudeNumber(Rational x, Rational y)
    {
        Rational ax = Abs(x);
        Rational ay = Abs(y);

        if ((ax < ay) || IsNaN(ay))
        {
            return x;
        }

        if (ax == ay)
        {
            return IsNegative(x) ? x : y;
        }

        return y;
    }

    public static Rational Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
    {
        int idx = s.IndexOf('/');
        if (idx == 0 || s.Contains('.'))
        {
            throw new ArgumentException("Invalid Rational", nameof(s));
        }

        if (idx > 0)
        {
            ReadOnlySpan<char> numStr = s.Slice(0, idx);
            ReadOnlySpan<char> denStr = s.Slice(idx + 1);
            long num = long.Parse(numStr, style, provider);
            long den = long.Parse(denStr, style, provider);
            return new Rational(num, den);
        }
        else
        {
            return new Rational(long.Parse(s, style, provider));
        }
    }

    public static Rational Parse(string s, NumberStyles style, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), style, provider);
    }

    public static Rational Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s, NumberStyles.Integer, provider);
    }

    public static Rational Parse(ReadOnlySpan<char> s)
    {
        return Parse(s);
    }

    public static Rational Parse(string s)
    {
        return Parse(s, null);
    }

    public static bool TryParse(ReadOnlySpan<char> s, out Rational result)
    {
        return TryParse(s, null, out result);
    }

    public static bool TryParse(string s, out Rational result)
    {
        return TryParse(s, null, out result);
    }

    public static Rational Parse(string s, IFormatProvider? provider)
    {
        return Parse(s, NumberStyles.Integer, provider);
    }

    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out Rational result)
    {
        result = default;
        int idx = s.IndexOf('/');
        if (idx == 0 || s.Contains('.'))
        {
            return false;
        }

        if (idx > 0)
        {
            ReadOnlySpan<char> numStr = s.Slice(0, idx);
            ReadOnlySpan<char> denStr = s.Slice(idx + 1);

            if (long.TryParse(numStr, style, provider, out long num)
                && long.TryParse(denStr, style, provider, out long den))
            {
                result = new Rational(num, den);
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (long.TryParse(s, style, provider, out long num))
            {
                result = new Rational(num);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out Rational result)
    {
        return TryParse(s.AsSpan(), style, provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out Rational result)
    {
        return TryParse(s, NumberStyles.Integer, provider, out result);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Rational result)
    {
        return TryParse(s, NumberStyles.Integer, provider, out result);
    }

    public static bool IsInteger(Rational value)
    {
        if (IsInfinity(value))
        {
            return false;
        }

        if (value.Denominator == 1)
        {
            return true;
        }

        return double.IsInteger(value.ToDouble());
    }

    public static bool IsNegativeInfinity(Rational value) => value.Denominator == 0 && value.Numerator == -1;

    public static bool IsPositiveInfinity(Rational value) => value.Denominator == 0 && value.Numerator == 1;

    public static bool IsZero(Rational value) => value.Denominator == 1 && value.Numerator == 0;

    static bool INumberBase<Rational>.TryConvertFromChecked<TOther>(TOther value, out Rational result)
    {
        return TryConvertFrom(value, out result);
    }

    static bool INumberBase<Rational>.TryConvertFromSaturating<TOther>(TOther value, out Rational result)
    {
        return TryConvertFrom(value, out result);
    }

    static bool INumberBase<Rational>.TryConvertFromTruncating<TOther>(TOther value, out Rational result)
    {
        return TryConvertFrom(value, out result);
    }

    private static bool TryConvertFrom<TOther>(TOther value, out Rational result)
        where TOther : INumberBase<TOther>
    {
        if (typeof(TOther) == typeof(double))
        {
            double actualValue = (double)(object)value;
            result = FromDouble(actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualValue = (Half)(object)value;
            result = FromDouble((double)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualValue = (short)(object)value;
            result = new Rational(actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)value;
            result = new Rational(actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)value;
            result = new Rational(actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualValue = (Int128)(object)value;
            result = new Rational((long)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualValue = (nint)(object)value;
            result = new Rational(actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(sbyte))
        {
            sbyte actualValue = (sbyte)(object)value;
            result = new Rational(actualValue);
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    static bool INumberBase<Rational>.TryConvertToChecked<TOther>(Rational value, [MaybeNullWhen(false)] out TOther result)
    {
        if (typeof(TOther) == typeof(byte))
        {
            byte actualResult = checked((byte)value.ToSingle());
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            char actualResult = checked((char)value.ToSingle());
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualResult = checked(value.ToDecimal());
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualResult = checked((ushort)value.ToSingle());
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualResult = checked((uint)value.ToSingle());
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualResult = checked((ulong)value.ToDouble());
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualResult = checked((UInt128)value.ToDecimal());
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualResult = checked((nuint)value.ToSingle());
            result = (TOther)(object)actualResult;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    static bool INumberBase<Rational>.TryConvertToSaturating<TOther>(Rational value, [MaybeNullWhen(false)] out TOther result)
    {
        return TryConvertTo(value, out result);
    }

    static bool INumberBase<Rational>.TryConvertToTruncating<TOther>(Rational value, [MaybeNullWhen(false)] out TOther result)
    {
        return TryConvertTo(value, out result);
    }

    private static bool TryConvertTo<TOther>(Rational value, [MaybeNullWhen(false)] out TOther result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `float` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types.

        if (typeof(TOther) == typeof(byte))
        {
            float valueF = value.ToSingle();
            byte actualResult = (valueF >= byte.MaxValue) ? byte.MaxValue :
                                (valueF <= byte.MinValue) ? byte.MinValue : (byte)valueF;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            float valueF = value.ToSingle();
            char actualResult = (valueF >= char.MaxValue) ? char.MaxValue :
                                (valueF <= char.MinValue) ? char.MinValue : (char)valueF;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualResult = IsNaN(value) ? 0.0m : value.ToDecimal();
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            float valueF = value.ToSingle();
            ushort actualResult = (valueF >= ushort.MaxValue) ? ushort.MaxValue :
                                  (valueF <= ushort.MinValue) ? ushort.MinValue : (ushort)valueF;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            float valueF = value.ToSingle();
            uint actualResult = (valueF >= uint.MaxValue) ? uint.MaxValue :
                                (valueF <= uint.MinValue) ? uint.MinValue : (uint)valueF;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            double valueD = value.ToDouble();
            ulong actualResult = (valueD >= ulong.MaxValue) ? ulong.MaxValue :
                                 (valueD <= ulong.MinValue) ? ulong.MinValue :
                                 IsNaN(value) ? 0 : (ulong)valueD;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualResult = (IsPositiveInfinity(value)) ? UInt128.MaxValue :
                                   (IsNegative(value) || IsZero(value)) ? UInt128.MinValue : (UInt128)value.Numerator / (UInt128)value.Denominator;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            float valueF = value.ToSingle();
            nuint actualResult = (valueF >= nuint.MaxValue) ? unchecked(nuint.MaxValue) :
                                 (valueF <= nuint.MinValue) ? unchecked(nuint.MinValue) : (nuint)valueF;
            result = (TOther)(object)actualResult;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    public int CompareTo(object? value)
    {
        if (value == null)
        {
            return 1;
        }

        if (value is Rational f)
        {
            if (this < f) return -1;
            if (this > f) return 1;
            if (this == f) return 0;

            // At least one of the values is NaN.
            if (IsNaN(this))
                return IsNaN(f) ? 0 : -1;
            else // f is NaN.
                return 1;
        }

        throw new ArgumentException();
    }

    public int CompareTo(Rational value)
    {
        if (this < value) return -1;
        if (this > value) return 1;
        if (this == value) return 0;

        // At least one of the values is NaN.
        if (IsNaN(this))
            return IsNaN(value) ? 0 : -1;
        else // f is NaN.
            return 1;
    }

    public static Rational operator +(Rational value) => value;

    public static Rational operator ++(Rational value)
    {
        long num = value.Numerator + value.Denominator;
        return new Rational(num, value.Denominator);
    }

    public static Rational operator --(Rational value)
    {
        long num = value.Numerator - value.Denominator;
        return new Rational(num, value.Denominator);
    }

    public static Rational operator %(Rational left, Rational right)
    {
        Reduce(ref left, ref right);

        long remainder = left.Numerator % right.Numerator;
        return new Rational(remainder, left.Denominator);
    }

    public static Rational operator %(Rational left, int right)
    {
        long remainder = left.Numerator % right;
        return new Rational(remainder, left.Denominator);
    }

    public static bool operator <(Rational left, Rational right)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return false;
        }

        if (IsInteger(left) && IsInteger(right))
        {
            return left.Numerator < right.Numerator;
        }

        return left.ToDouble() < right.ToDouble();
    }

    public static bool operator >(Rational left, Rational right)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return false;
        }

        if (IsInteger(left) && IsInteger(right))
        {
            return left.Numerator > right.Numerator;
        }

        return left.ToDouble() > right.ToDouble();
    }

    public static bool operator <=(Rational left, Rational right)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return false;
        }

        if (IsInteger(left) && IsInteger(right))
        {
            return left.Numerator <= right.Numerator;
        }

        return left.ToDouble() <= right.ToDouble();
    }

    public static bool operator >=(Rational left, Rational right)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return false;
        }

        if (IsInteger(left) && IsInteger(right))
        {
            return left.Numerator >= right.Numerator;
        }

        return left.ToDouble() >= right.ToDouble();
    }
}
