using System.Text;

using Avalonia.Input;

using Beutl.Graphics;

namespace Beutl.Controls.PropertyEditors;

internal static class RelativeUnitParser
{
    public static bool TryParse(string? s, out float result, out RelativeUnit unit)
    {
        if (!TryStripPercentUnits(s, out string expression, out bool hasPercentUnit))
        {
            result = default;
            unit = default;
            return false;
        }

        unit = hasPercentUnit ? RelativeUnit.Relative : RelativeUnit.Absolute;
        float scale = hasPercentUnit ? 0.01f : 1f;

        if (NumberEditorHelper.TryParse(expression, out float value))
        {
            result = value * scale;
            return true;
        }
        else
        {
            result = 1f;
            return false;
        }
    }

    // Removes '%' unit suffixes (e.g. "50%", "50+25%", "50.00%/2", "50% + 25%") while keeping binary modulo
    // operators (e.g. "100 % 3"). Rejects standalone or repeated percent signs such as "%" or "50%%".
    internal static bool TryStripPercentUnits(string? s, out string stripped, out bool hasPercentUnit)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            stripped = string.Empty;
            hasPercentUnit = false;
            return false;
        }

        if (!s.Contains('%'))
        {
            stripped = s;
            hasPercentUnit = false;
            return true;
        }

        var builder = new StringBuilder(s.Length);
        hasPercentUnit = false;
        char lastNonWhitespace = '\0';

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '%')
            {
                if (lastNonWhitespace is '\0' or '%')
                {
                    stripped = string.Empty;
                    hasPercentUnit = false;
                    return false;
                }

                char nextNonWhitespace = '\0';
                for (int j = i + 1; j < s.Length; j++)
                {
                    if (!char.IsWhiteSpace(s[j]))
                    {
                        nextNonWhitespace = s[j];
                        break;
                    }
                }

                if (nextNonWhitespace == '%')
                {
                    stripped = string.Empty;
                    hasPercentUnit = false;
                    return false;
                }

                bool precedesUnitEnd = nextNonWhitespace is '\0' or '+' or '-' or '*' or '/' or '^' or ')';
                bool followsOperand = char.IsAsciiDigit(lastNonWhitespace) || lastNonWhitespace is '.' or ',' or ')';
                if (followsOperand && precedesUnitEnd)
                {
                    hasPercentUnit = true;
                    lastNonWhitespace = '%';
                    continue;
                }
            }

            builder.Append(c);
            if (!char.IsWhiteSpace(c))
            {
                lastNonWhitespace = c;
            }
        }

        stripped = builder.ToString();
        return true;
    }

    // A uniform editor can apply the same value to the other components even when this one stayed the same.
    public static bool TryParseEdit(
        string? newText,
        string? oldText,
        out float newValue,
        out RelativeUnit newUnit,
        out float oldValue,
        out RelativeUnit oldUnit,
        bool allowSameValue)
    {
        if (TryParse(newText, out newValue, out newUnit))
        {
            bool invalidOldValue = !TryParse(oldText, out oldValue, out oldUnit);
            if (invalidOldValue)
            {
                oldValue = newValue;
                oldUnit = newUnit;
            }

            return invalidOldValue || allowSameValue || newValue != oldValue || newUnit != oldUnit;
        }

        oldValue = default;
        oldUnit = default;
        return false;
    }

    public static float StepByWheel(float value, RelativeUnit unit, PointerWheelEventArgs e)
    {
        float delta1 = 1;
        float delta2 = 10;
        if (unit == RelativeUnit.Relative)
        {
            delta1 *= 0.01f;
            delta2 *= 0.01f;
        }

        float delta3 = delta2;
        var wheelDelta = e.Delta.Y;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            delta3 = delta1;
            wheelDelta = -e.Delta.X;
        }

        return wheelDelta switch
        {
            < 0 => value - delta3,
            > 0 => value + delta3,
            _ => value
        };
    }
}
