using System.Globalization;

using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

internal static class TransformParser
{
    private static readonly (string, TransformFunction)[] s_functionMapping =
    [
        ("translate", TransformFunction.Translate),
        ("translateX", TransformFunction.TranslateX),
        ("translateY", TransformFunction.TranslateY),
        ("scale", TransformFunction.Scale),
        ("scaleX", TransformFunction.ScaleX),
        ("scaleY", TransformFunction.ScaleY),
        ("skew", TransformFunction.Skew),
        ("skewX", TransformFunction.SkewX),
        ("skewY", TransformFunction.SkewY),
        ("rotate", TransformFunction.Rotate),
        ("matrix", TransformFunction.Matrix)
    ];

    private static readonly (string, Unit)[] s_unitMapping =
    [
        ("deg", Unit.Degree),
        ("grad", Unit.Gradian),
        ("rad", Unit.Radian),
        ("turn", Unit.Turn),
        ("px", Unit.Pixel),
        ("%", Unit.Relative),
        ("", Unit.None),
    ];

    public static Matrix ParseMatrix(ReadOnlySpan<char> s)
    {
        static void ThrowInvalidFormat(ReadOnlySpan<char> s)
        {
            throw new FormatException($"Invalid transform string: '{s.ToString()}'.");
        }

        if (s.Length <= 0)
        {
            throw new ArgumentException(null, nameof(s));
        }

        ReadOnlySpan<char> span = s.Trim();

        if (span.Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return Matrix.Identity;
        }

        Matrix result = Matrix.Identity;

        while (true)
        {
            int beginIndex = span.IndexOf('(');
            int endIndex = span.IndexOf(')');

            if (beginIndex == -1 || endIndex == -1)
            {
                ThrowInvalidFormat(s);
            }

            ReadOnlySpan<char> namePart = span.Slice(0, beginIndex).Trim();

            TransformFunction function = ParseTransformFunction(in namePart);

            if (function == TransformFunction.Invalid)
            {
                ThrowInvalidFormat(s);
            }

            ReadOnlySpan<char> valuePart = span.Slice(beginIndex + 1, endIndex - beginIndex - 1).Trim();

            ParseFunction(in valuePart, function, ref result);

            span = span.Slice(endIndex + 1);

            if (span.IsWhiteSpace())
            {
                break;
            }
        }

        return result;
    }

    private static void ParseFunction(
        in ReadOnlySpan<char> functionPart,
        TransformFunction function,
        ref Matrix result)
    {
        switch (function)
        {
            case TransformFunction.Scale:
            case TransformFunction.ScaleX:
            case TransformFunction.ScaleY:
                ParseScaleFunction(functionPart, function, ref result);
                break;
            case TransformFunction.Skew:
            case TransformFunction.SkewX:
            case TransformFunction.SkewY:
                ParseSkewFunction(functionPart, function, ref result);
                break;
            case TransformFunction.Rotate:
                ParseRotateFunction(functionPart, function, ref result);
                break;
            case TransformFunction.Translate:
            case TransformFunction.TranslateX:
            case TransformFunction.TranslateY:
                ParseTranslateFunction(functionPart, function, ref result);
                break;
            case TransformFunction.Matrix:
                ParseMatrixFunction(functionPart, function, ref result);
                break;
        }
    }

    private static void ParseScaleFunction(
        in ReadOnlySpan<char> functionPart,
        TransformFunction function,
        ref Matrix result)
    {
        UnitValue scaleX = UnitValue.One;
        UnitValue scaleY = UnitValue.One;

        int count = ParseValuePair(functionPart, ref scaleX, ref scaleY);

        if (count != 1 && (function == TransformFunction.ScaleX || function == TransformFunction.ScaleY))
        {
            ThrowFormatInvalidValueCount(function, 1);
        }

        VerifyZeroOrScale(function, in scaleX);
        VerifyZeroOrScale(function, in scaleY);

        if (function == TransformFunction.ScaleY)
        {
            scaleY = scaleX;
            scaleX = UnitValue.One;
        }
        else if (function == TransformFunction.Scale && count == 1)
        {
            scaleY = scaleX;
        }

        result = Matrix.CreateScale(ToScale(in scaleX), ToScale(in scaleY)) * result;
    }

    private static void ParseSkewFunction(
        in ReadOnlySpan<char> functionPart,
        TransformFunction function,
        ref Matrix result)
    {
        UnitValue skewX = UnitValue.Zero;
        UnitValue skewY = UnitValue.Zero;

        int count = ParseValuePair(functionPart, ref skewX, ref skewY);

        if (count != 1 && (function == TransformFunction.SkewX || function == TransformFunction.SkewY))
        {
            ThrowFormatInvalidValueCount(function, 1);
        }

        VerifyZeroOrAngle(function, in skewX);
        VerifyZeroOrAngle(function, in skewY);

        if (function == TransformFunction.SkewY)
        {
            skewY = skewX;
            skewX = UnitValue.Zero;
        }

        result = Matrix.CreateSkew(ToRadians(in skewX), ToRadians(in skewY)) * result;
    }

    private static void ParseRotateFunction(
        in ReadOnlySpan<char> functionPart,
        TransformFunction function,
        ref Matrix result)
    {
        UnitValue angle = UnitValue.Zero;
        UnitValue _ = default;

        int count = ParseValuePair(functionPart, ref angle, ref _);

        if (count != 1)
        {
            ThrowFormatInvalidValueCount(function, 1);
        }

        VerifyZeroOrAngle(function, in angle);

        result = Matrix.CreateRotation(ToRadians(in angle)) * result;
    }

    private static void ParseTranslateFunction(
        in ReadOnlySpan<char> functionPart,
        TransformFunction function,
        ref Matrix result)
    {
        UnitValue translateX = UnitValue.Zero;
        UnitValue translateY = UnitValue.Zero;

        int count = ParseValuePair(functionPart, ref translateX, ref translateY);

        if (count != 1 && (function == TransformFunction.TranslateX || function == TransformFunction.TranslateY))
        {
            ThrowFormatInvalidValueCount(function, 1);
        }

        VerifyZeroOrUnit(function, in translateX, Unit.Pixel);
        VerifyZeroOrUnit(function, in translateY, Unit.Pixel);

        if (function == TransformFunction.TranslateY)
        {
            translateY = translateX;
            translateX = UnitValue.Zero;
        }

        result = Matrix.CreateTranslation(translateX.Value, translateY.Value) * result;
    }

    private static void ParseMatrixFunction(
        in ReadOnlySpan<char> functionPart,
        TransformFunction function,
        ref Matrix result)
    {
        Span<UnitValue> values = stackalloc UnitValue[9];

        int count = ParseCommaDelimitedValues(functionPart, in values);

        if (count is not (6 or 9))
        {
            ThrowFormatInvalidValueCount(function, 6);
        }

        foreach (UnitValue value in values)
        {
            VerifyZeroOrUnit(function, value, Unit.None);
        }

        if (count == 6)
        {
            var matrix = new Matrix(
                values[0].Value, values[1].Value,
                values[2].Value, values[3].Value,
                values[4].Value, values[5].Value);

            result = matrix * result;
        }
        else
        {
            var matrix = new Matrix(
                values[0].Value, values[1].Value, values[2].Value,
                values[3].Value, values[4].Value, values[5].Value,
                values[6].Value, values[7].Value, values[8].Value);

            result = matrix * result;
        }
    }

    private static UnitValue ParseUnitValue(ReadOnlySpan<char> part)
    {
        int unitIndex = -1;

        for (int i = 0; i < part.Length; i++)
        {
            char c = part[i];

            if (char.IsDigit(c) || c == '-' || c == '.')
            {
                continue;
            }

            unitIndex = i;
            break;
        }

        Unit unit = Unit.None;

        if (unitIndex != -1)
        {
            ReadOnlySpan<char> unitPart = part.Slice(unitIndex, part.Length - unitIndex);

            unit = ParseUnit(unitPart);

            part = part.Slice(0, unitIndex);
        }

        float value = float.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture);

        return new UnitValue(unit, value);
    }

    private static int ParseValuePair(
        in ReadOnlySpan<char> part,
        ref UnitValue leftValue,
        ref UnitValue rightValue)
    {
        int commaIndex = part.IndexOf(',');

        if (commaIndex != -1)
        {
            ReadOnlySpan<char> leftPart = part.Slice(0, commaIndex).Trim();
            ReadOnlySpan<char> rightPart = part.Slice(commaIndex + 1, part.Length - commaIndex - 1).Trim();

            leftValue = ParseUnitValue(leftPart);
            rightValue = ParseUnitValue(rightPart);

            return 2;
        }

        leftValue = ParseUnitValue(part);

        return 1;
    }

    private static int ParseCommaDelimitedValues(ReadOnlySpan<char> part, in Span<UnitValue> outValues)
    {
        int valueIndex = 0;

        while (true)
        {
            if (valueIndex >= outValues.Length)
            {
                throw new FormatException("Too many provided values.");
            }

            int commaIndex = part.IndexOf(',');

            if (commaIndex == -1)
            {
                if (!part.IsWhiteSpace())
                {
                    outValues[valueIndex++] = ParseUnitValue(part);
                }

                break;
            }

            ReadOnlySpan<char> valuePart = part.Slice(0, commaIndex).Trim();

            outValues[valueIndex++] = ParseUnitValue(valuePart);

            part = part.Slice(commaIndex + 1, part.Length - commaIndex - 1).Trim();
        }

        return valueIndex;
    }

    private static void VerifyZeroOrUnit(TransformFunction function, in UnitValue value, Unit unit)
    {
        if (value.Value != 0f && value.Unit != unit)
        {
            ThrowFormatInvalidValue(function, in value);
        }
    }

    private static void VerifyZeroOrScale(TransformFunction function, in UnitValue value)
    {
        if (value.Value != 0f && !IsScaleUnit(value.Unit))
        {
            ThrowFormatInvalidValue(function, in value);
        }
    }

    private static void VerifyZeroOrAngle(TransformFunction function, in UnitValue value)
    {
        if (value.Value != 0f && !IsAngleUnit(value.Unit))
        {
            ThrowFormatInvalidValue(function, in value);
        }
    }

    private static bool IsAngleUnit(Unit unit)
    {
        return unit is Unit.Radian or Unit.Gradian or Unit.Degree or Unit.Turn;
    }

    private static bool IsScaleUnit(Unit unit)
    {
        return unit is Unit.None or Unit.Relative;
    }

    private static void ThrowFormatInvalidValue(TransformFunction function, in UnitValue value)
    {
        string? unitString = value.Unit == Unit.None ? string.Empty : value.Unit.ToString();

        throw new FormatException($"Invalid value {value.Value} {unitString} for {function}");
    }

    private static void ThrowFormatInvalidValueCount(TransformFunction function, int count)
    {
        throw new FormatException($"Invalid format. {function} expects {count} value(s).");
    }

    private static Unit ParseUnit(in ReadOnlySpan<char> part)
    {
        foreach ((string name, Unit unit) in s_unitMapping)
        {
            if (part.Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return unit;
            }
        }

        throw new FormatException($"Invalid unit: {part.ToString()}");
    }

    private static TransformFunction ParseTransformFunction(in ReadOnlySpan<char> part)
    {
        foreach ((string name, TransformFunction transformFunction) in s_functionMapping)
        {
            if (part.Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return transformFunction;
            }
        }

        return TransformFunction.Invalid;
    }

    private static float ToRadians(in UnitValue value)
    {
        return value.Unit switch
        {
            Unit.Radian => value.Value,
            Unit.Gradian => MathUtilities.Grad2Rad(value.Value),
            Unit.Degree => MathUtilities.Deg2Rad(value.Value),
            Unit.Turn => MathUtilities.Turn2Rad(value.Value),
            _ => value.Value
        };
    }

    private static float ToScale(in UnitValue value)
    {
        return value.Unit switch
        {
            Unit.None => value.Value,
            Unit.Relative => value.Value / 100,
            _ => value.Value
        };
    }

    private enum Unit
    {
        None,
        Pixel,
        Radian,
        Gradian,
        Degree,
        Turn,
        Relative
    }

    private readonly struct UnitValue(Unit unit, float value)
    {
        public readonly Unit Unit = unit;
        public readonly float Value = value;

        public static UnitValue Zero => new(Unit.None, 0);

        public static UnitValue One => new(Unit.None, 1);
    }

    private enum TransformFunction
    {
        Invalid,
        Translate,
        TranslateX,
        TranslateY,
        Scale,
        ScaleX,
        ScaleY,
        Skew,
        SkewX,
        SkewY,
        Rotate,
        Matrix
    }
}
