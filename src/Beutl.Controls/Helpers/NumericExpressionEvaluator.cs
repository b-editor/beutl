using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Beutl;

// Evaluates simple arithmetic expressions (+, -, *, /, %, ^, unary +/-, and parentheses) for numeric editors.
// Uses decimal arithmetic first so expressions such as "0.1 + 0.2" or "100 * 1.15" stay exact, falling back to
// double when decimal overflows or an exponent is fractional.
internal static class NumericExpressionEvaluator
{
    private const int MaxDepth = 64;

    // Below 1e-12, decimal's fixed 28-decimal-place scale leaves fewer than ~16 significant digits (worse than IEEE 754 double).
    private const decimal MinAccurateDecimalMagnitude = 1e-12m;

    // 10^24: a decimal whose unscaled mantissa reaches this magnitude with a fractional scale has saturated
    // decimal's 28-digit precision (e.g. from repeating division such as 1 / 3 * 4.5).
    private static readonly UInt128 SaturatedDecimalMantissa = (UInt128)1_000_000_000_000UL * 1_000_000_000_000UL;

    private static bool IsBelowAccurateDecimalMagnitude(decimal value)
        => value != 0m && Math.Abs(value) < MinAccurateDecimalMagnitude;

    private static bool LostPrecisionAgainstDouble(decimal decimalValue, double expectedDouble)
        => double.IsFinite(expectedDouble) && (double)decimalValue != expectedDouble;

    // Whether a nonzero operation's decimal result vanished, or is too small to keep the precision double gives.
    private static bool LostToDecimal(decimal value, double expectedDouble)
        => value == 0m || (IsBelowAccurateDecimalMagnitude(value) && LostPrecisionAgainstDouble(value, expectedDouble));

    // Whether multiplying the quotient back misses the dividend; an overflow counts as a miss.
    private static bool IsInexactQuotient(decimal quotient, decimal divisor, decimal dividend)
    {
        try
        {
            return quotient * divisor != dividend;
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    private enum EvalStatus
    {
        Success,
        SyntaxOrMathError,
        NeedsDoubleFallback,
    }

    public static bool TryEvaluate<TValue>(ReadOnlySpan<char> text, [MaybeNullWhen(false)] out TValue value)
        where TValue : INumberBase<TValue>
    {
        text = text.Trim();
        if (text.IsEmpty || text.IndexOfAny("+-*/%^()") < 0)
        {
            value = default;
            return false;
        }

        var decimalParser = new DecimalParser(text);
        EvalStatus status = decimalParser.TryParse(out decimal decimalResult);
        if (status == EvalStatus.Success)
        {
            return TryConvertFromDecimal(decimalResult, decimalParser.HasInexactDivision, out value);
        }

        if (status == EvalStatus.NeedsDoubleFallback)
        {
            var doubleParser = new DoubleParser(text);
            if (doubleParser.TryParse(out double doubleResult))
            {
                return TryConvertFromDouble(doubleResult, out value);
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseDecimalLiteral(ReadOnlySpan<char> span, out decimal value)
    {
        return decimal.TryParse(span, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(span, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseDoubleLiteral(ReadOnlySpan<char> span, out double value)
    {
        bool parsed = double.TryParse(span, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(span, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.CurrentCulture, out value);
        return parsed && double.IsFinite(value);
    }

    private static bool TryScanNumber(ReadOnlySpan<char> text, ref int pos, out ReadOnlySpan<char> token)
    {
        NumberFormatInfo format = CultureInfo.CurrentCulture.NumberFormat;
        string decimalSeparator = format.NumberDecimalSeparator;
        string groupSeparator = format.NumberGroupSeparator;

        int start = pos;
        bool hasDigit = false;

        while (pos < text.Length)
        {
            char c = text[pos];
            if (char.IsAsciiDigit(c))
            {
                hasDigit = true;
                pos++;
            }
            else if (c is '.' or ',')
            {
                pos++;
            }
            else if (decimalSeparator.Length > 0
                && decimalSeparator is not ("." or ",")
                && text[pos..].StartsWith(decimalSeparator, StringComparison.Ordinal))
            {
                pos += decimalSeparator.Length;
            }
            else if (hasDigit
                && groupSeparator.Length > 0
                && groupSeparator is not ("." or ",")
                && text[pos..].StartsWith(groupSeparator, StringComparison.Ordinal)
                && pos + groupSeparator.Length < text.Length
                && char.IsAsciiDigit(text[pos + groupSeparator.Length]))
            {
                pos += groupSeparator.Length;
            }
            else if (c is 'e' or 'E')
            {
                if (!hasDigit)
                {
                    break;
                }

                pos++;
                if (pos < text.Length && text[pos] is '+' or '-')
                {
                    pos++;
                }

                bool hasExpDigit = false;
                while (pos < text.Length && char.IsAsciiDigit(text[pos]))
                {
                    hasExpDigit = true;
                    pos++;
                }

                if (!hasExpDigit)
                {
                    token = default;
                    return false;
                }

                break;
            }
            else
            {
                break;
            }
        }

        if (!hasDigit)
        {
            token = default;
            return false;
        }

        token = text[start..pos];
        return true;
    }

    private static decimal NormalizeDecimal(decimal value)
    {
        if (value == 0m)
        {
            return 0m;
        }

        int scale = value.Scale;
        if (scale == 0)
        {
            return value;
        }

        UInt128 mantissa = GetMantissa(value);
        while (scale > 0 && mantissa % 10 == 0)
        {
            mantissa /= 10;
            scale--;
        }

        ulong low64 = (ulong)mantissa;
        uint high32 = (uint)(mantissa >> 64);
        return new decimal((int)(uint)low64, (int)(uint)(low64 >> 32), (int)high32, decimal.IsNegative(value), (byte)scale);
    }

    // The unscaled 96-bit magnitude of a decimal.
    private static UInt128 GetMantissa(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        return new UInt128(
            (uint)bits[2],
            ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
    }

    private static decimal RoundDecimalToInteger(decimal value, bool hasInexactDivision)
    {
        if (hasInexactDivision)
        {
            int scale = value.Scale;
            if (scale >= 12 && GetMantissa(value) >= SaturatedDecimalMantissa)
            {
                // Trim only the trailing 2 digits of 28-digit division rounding noise so exact midpoints like
                // 1 / 3 * 4.5 (1.4999999999999999999999999998) round away from zero to 2 without disturbing
                // intentional sub-midpoint fractions on large integers.
                value = Math.Round(value, scale - 2, MidpointRounding.AwayFromZero);
            }
        }

        return Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static bool TryConvertFromDecimal<TValue>(decimal value, bool hasInexactDivision, [MaybeNullWhen(false)] out TValue result)
        where TValue : INumberBase<TValue>
    {
        if (typeof(TValue) == typeof(decimal))
        {
            decimal normalized = NormalizeDecimal(value);
            result = Unsafe.As<decimal, TValue>(ref normalized);
            return true;
        }

        if (typeof(TValue) == typeof(float))
        {
            float f = (float)value;
            if (f == 0) f = 0;
            result = Unsafe.As<float, TValue>(ref f);
            return true;
        }

        if (NumberEditorHelper.IsFloatingPoint<TValue>())
        {
            return TryConvertFromDouble((double)value, out result);
        }

        decimal rounded = RoundDecimalToInteger(value, hasInexactDivision);
        try
        {
            result = TValue.CreateChecked(rounded);
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static bool TryConvertFromDouble<TValue>(double value, [MaybeNullWhen(false)] out TValue result)
        where TValue : INumberBase<TValue>
    {
        if (!double.IsFinite(value))
        {
            result = default;
            return false;
        }

        if (value == 0) value = 0;

        if (typeof(TValue) == typeof(double))
        {
            result = Unsafe.As<double, TValue>(ref value);
            return true;
        }

        if (typeof(TValue) == typeof(float))
        {
            float f = (float)value;
            if (!float.IsFinite(f))
            {
                result = default;
                return false;
            }

            if (f == 0) f = 0;
            result = Unsafe.As<float, TValue>(ref f);
            return true;
        }

        if (typeof(TValue) == typeof(decimal))
        {
            try
            {
                decimal d = NormalizeDecimal((decimal)value);
                result = Unsafe.As<decimal, TValue>(ref d);
                return true;
            }
            catch (OverflowException)
            {
                result = default;
                return false;
            }
        }

        if (NumberEditorHelper.IsFloatingPoint<TValue>())
        {
            try
            {
                result = TValue.CreateChecked(value);
                if (!double.IsFinite(double.CreateTruncating(result)))
                {
                    result = default;
                    return false;
                }

                return true;
            }
            catch (OverflowException)
            {
                result = default;
                return false;
            }
        }

        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        try
        {
            result = TValue.CreateChecked(rounded);
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private ref struct DecimalParser(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _pos = 0;
        private int _depth = 0;
        private bool _hasExpressionSyntax = false;
        private bool _hasInexactDivision = false;

        public readonly bool HasInexactDivision => _hasInexactDivision;

        public EvalStatus TryParse(out decimal value)
        {
            EvalStatus status = ParseAdditive(out value);
            if (status != EvalStatus.Success)
            {
                return status;
            }

            SkipWhitespace();
            return _pos == _text.Length && _hasExpressionSyntax
                ? EvalStatus.Success
                : EvalStatus.SyntaxOrMathError;
        }

        private EvalStatus ParseAdditive(out decimal value)
        {
            EvalStatus status = ParseMultiplicative(out value);
            if (status != EvalStatus.Success)
            {
                return status;
            }

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] is not ('+' or '-'))
                {
                    return EvalStatus.Success;
                }

                char op = _text[_pos++];
                _hasExpressionSyntax = true;
                status = ParseMultiplicative(out decimal right);
                if (status != EvalStatus.Success)
                {
                    return status;
                }

                try
                {
                    value = op == '+' ? value + right : value - right;
                }
                catch (OverflowException)
                {
                    return EvalStatus.NeedsDoubleFallback;
                }
            }
        }

        private EvalStatus ParseMultiplicative(out decimal value)
        {
            EvalStatus status = ParseUnary(out value, unaryCount: 0);
            if (status != EvalStatus.Success)
            {
                return status;
            }

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] is not ('*' or '/' or '%'))
                {
                    return EvalStatus.Success;
                }

                char op = _text[_pos++];
                _hasExpressionSyntax = true;
                status = ParseUnary(out decimal right, unaryCount: 0);
                if (status != EvalStatus.Success)
                {
                    return status;
                }

                if (op is '/' or '%' && right == 0m)
                {
                    return EvalStatus.SyntaxOrMathError;
                }

                try
                {
                    decimal left = value;
                    value = op switch
                    {
                        '*' => left * right,
                        '/' => left / right,
                        _ => left % right,
                    };
                    if (op == '*')
                    {
                        if (left != 0m && right != 0m && LostToDecimal(value, (double)left * (double)right))
                        {
                            return EvalStatus.NeedsDoubleFallback;
                        }
                    }
                    else if (op == '/')
                    {
                        if (left != 0m && LostToDecimal(value, (double)left / (double)right))
                        {
                            return EvalStatus.NeedsDoubleFallback;
                        }

                        if (!_hasInexactDivision && IsInexactQuotient(value, right, left))
                        {
                            _hasInexactDivision = true;
                        }
                    }
                }
                catch (OverflowException)
                {
                    return EvalStatus.NeedsDoubleFallback;
                }
            }
        }

        private EvalStatus ParseUnary(out decimal value, int unaryCount)
        {
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] is '+' or '-')
            {
                char op = _text[_pos++];
                if (unaryCount > 0)
                {
                    _hasExpressionSyntax = true;
                }

                if (++_depth > MaxDepth)
                {
                    value = 0;
                    return EvalStatus.SyntaxOrMathError;
                }

                EvalStatus status = ParseUnary(out value, unaryCount + 1);
                _depth--;
                if (status != EvalStatus.Success)
                {
                    return status;
                }

                if (op == '-')
                {
                    value = -value;
                }

                return EvalStatus.Success;
            }

            return ParsePower(out value);
        }

        private EvalStatus ParsePower(out decimal value)
        {
            EvalStatus status = ParsePrimary(out value);
            if (status != EvalStatus.Success)
            {
                return status;
            }

            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == '^')
            {
                _pos++;
                _hasExpressionSyntax = true;
                if (++_depth > MaxDepth)
                {
                    return EvalStatus.SyntaxOrMathError;
                }

                status = ParseUnary(out decimal exponent, unaryCount: 0);
                _depth--;
                if (status != EvalStatus.Success)
                {
                    return status;
                }

                return TryPowDecimal(value, exponent, out value, ref _hasInexactDivision);
            }

            return EvalStatus.Success;
        }

        private EvalStatus ParsePrimary(out decimal value)
        {
            SkipWhitespace();
            if (_pos >= _text.Length)
            {
                value = 0;
                return EvalStatus.SyntaxOrMathError;
            }

            if (_text[_pos] == '(')
            {
                _pos++;
                _hasExpressionSyntax = true;
                if (++_depth > MaxDepth)
                {
                    value = 0;
                    return EvalStatus.SyntaxOrMathError;
                }

                EvalStatus status = ParseAdditive(out value);
                _depth--;
                if (status != EvalStatus.Success)
                {
                    return status;
                }

                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != ')')
                {
                    return EvalStatus.SyntaxOrMathError;
                }

                _pos++;
                return EvalStatus.Success;
            }

            if (!TryScanNumber(_text, ref _pos, out ReadOnlySpan<char> token))
            {
                value = 0;
                return EvalStatus.SyntaxOrMathError;
            }

            if (TryParseDecimalLiteral(token, out value))
            {
                if ((value == 0m || IsBelowAccurateDecimalMagnitude(value))
                    && TryParseDoubleLiteral(token, out double doubleValue)
                    && (double)value != doubleValue)
                {
                    return EvalStatus.NeedsDoubleFallback;
                }

                return EvalStatus.Success;
            }

            return TryParseDoubleLiteral(token, out _)
                ? EvalStatus.NeedsDoubleFallback
                : EvalStatus.SyntaxOrMathError;
        }

        private static EvalStatus TryPowDecimal(decimal baseValue, decimal exponent, out decimal result, ref bool hasInexactDivision)
        {
            if (exponent == 0m)
            {
                result = 1m;
                return EvalStatus.Success;
            }

            if (baseValue == 0m && exponent < 0m)
            {
                result = 0m;
                return EvalStatus.SyntaxOrMathError;
            }

            if (exponent != decimal.Truncate(exponent))
            {
                result = 0m;
                return EvalStatus.NeedsDoubleFallback;
            }

            if (baseValue == 1m)
            {
                result = 1m;
                return EvalStatus.Success;
            }

            if (baseValue == 0m)
            {
                result = 0m;
                return EvalStatus.Success;
            }

            if (baseValue == -1m)
            {
                result = exponent % 2m == 0m ? 1m : -1m;
                return EvalStatus.Success;
            }

            if (exponent < -1000m || exponent > 1000m)
            {
                result = 0m;
                return EvalStatus.NeedsDoubleFallback;
            }

            int n = (int)exponent;
            bool negative = n < 0;
            int remaining = negative ? -n : n;

            try
            {
                decimal acc = 1m;
                decimal factor = baseValue;
                while (remaining > 0)
                {
                    if ((remaining & 1) != 0)
                    {
                        decimal prevAcc = acc;
                        acc *= factor;
                        if (LostToDecimal(acc, (double)prevAcc * (double)factor))
                        {
                            result = 0m;
                            return EvalStatus.NeedsDoubleFallback;
                        }
                    }

                    remaining >>= 1;
                    if (remaining > 0)
                    {
                        decimal prevFactor = factor;
                        factor *= factor;
                        if (LostToDecimal(factor, (double)prevFactor * (double)prevFactor))
                        {
                            result = 0m;
                            return EvalStatus.NeedsDoubleFallback;
                        }
                    }
                }

                if (negative)
                {
                    result = 1m / acc;
                    if (LostToDecimal(result, 1d / (double)acc))
                    {
                        return EvalStatus.NeedsDoubleFallback;
                    }

                    if (!hasInexactDivision && IsInexactQuotient(result, acc, 1m))
                    {
                        hasInexactDivision = true;
                    }
                }
                else
                {
                    result = acc;
                }

                return EvalStatus.Success;
            }
            catch (OverflowException)
            {
                result = 0m;
                return EvalStatus.NeedsDoubleFallback;
            }
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }
    }

    private ref struct DoubleParser(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _pos = 0;
        private int _depth = 0;
        private bool _hasExpressionSyntax = false;

        public bool TryParse(out double value)
        {
            if (!ParseAdditive(out value))
            {
                return false;
            }

            SkipWhitespace();
            return _pos == _text.Length && _hasExpressionSyntax && double.IsFinite(value);
        }

        private bool ParseAdditive(out double value)
        {
            if (!ParseMultiplicative(out value))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] is not ('+' or '-'))
                {
                    return true;
                }

                char op = _text[_pos++];
                _hasExpressionSyntax = true;
                if (!ParseMultiplicative(out double right))
                {
                    return false;
                }

                value = op == '+' ? value + right : value - right;
                if (!double.IsFinite(value))
                {
                    return false;
                }
            }
        }

        private bool ParseMultiplicative(out double value)
        {
            if (!ParseUnary(out value, unaryCount: 0))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] is not ('*' or '/' or '%'))
                {
                    return true;
                }

                char op = _text[_pos++];
                _hasExpressionSyntax = true;
                if (!ParseUnary(out double right, unaryCount: 0))
                {
                    return false;
                }

                if (op is '/' or '%' && right == 0d)
                {
                    return false;
                }

                value = op switch
                {
                    '*' => value * right,
                    '/' => value / right,
                    _ => value % right,
                };
                if (!double.IsFinite(value))
                {
                    return false;
                }
            }
        }

        private bool ParseUnary(out double value, int unaryCount)
        {
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] is '+' or '-')
            {
                char op = _text[_pos++];
                if (unaryCount > 0)
                {
                    _hasExpressionSyntax = true;
                }

                if (++_depth > MaxDepth)
                {
                    value = 0;
                    return false;
                }

                bool ok = ParseUnary(out value, unaryCount + 1);
                _depth--;
                if (!ok)
                {
                    return false;
                }

                if (op == '-')
                {
                    value = -value;
                }

                return true;
            }

            return ParsePower(out value);
        }

        private bool ParsePower(out double value)
        {
            if (!ParsePrimary(out value))
            {
                return false;
            }

            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == '^')
            {
                _pos++;
                _hasExpressionSyntax = true;
                if (++_depth > MaxDepth)
                {
                    return false;
                }

                bool ok = ParseUnary(out double exponent, unaryCount: 0);
                _depth--;
                if (!ok)
                {
                    return false;
                }

                value = Math.Pow(value, exponent);
                return double.IsFinite(value);
            }

            return true;
        }

        private bool ParsePrimary(out double value)
        {
            SkipWhitespace();
            if (_pos >= _text.Length)
            {
                value = 0;
                return false;
            }

            if (_text[_pos] == '(')
            {
                _pos++;
                _hasExpressionSyntax = true;
                if (++_depth > MaxDepth)
                {
                    value = 0;
                    return false;
                }

                bool ok = ParseAdditive(out value);
                _depth--;
                if (!ok)
                {
                    return false;
                }

                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != ')')
                {
                    return false;
                }

                _pos++;
                return true;
            }

            if (!TryScanNumber(_text, ref _pos, out ReadOnlySpan<char> token))
            {
                value = 0;
                return false;
            }

            return TryParseDoubleLiteral(token, out value);
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }
    }
}
