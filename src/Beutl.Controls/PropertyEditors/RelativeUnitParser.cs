using Avalonia.Input;

using Beutl.Graphics;

namespace Beutl.Controls.PropertyEditors;

internal static class RelativeUnitParser
{
    public static bool TryParse(string? s, out float result, out RelativeUnit unit)
    {
        if (s == null)
        {
            result = default;
            unit = default;
            return false;
        }

        result = 1f;
        float scale = 1f;
        ReadOnlySpan<char> span = s;

        if (s.EndsWith('%'))
        {
            scale = 0.01f;
            span = s.AsSpan()[0..^1];
            unit = RelativeUnit.Relative;
        }
        else
        {
            unit = RelativeUnit.Absolute;
        }

        if (float.TryParse(span, out float value))
        {
            result = value * scale;
            return true;
        }
        else
        {
            return false;
        }
    }

    // An edit applies when the new text parses and either the old text did not parse or the value changed.
    public static bool TryParseEdit(
        string? newText,
        string? oldText,
        out float newValue,
        out RelativeUnit newUnit,
        out float oldValue,
        out RelativeUnit oldUnit)
    {
        if (TryParse(newText, out newValue, out newUnit))
        {
            bool invalidOldValue = !TryParse(oldText, out oldValue, out oldUnit);
            if (invalidOldValue)
            {
                oldValue = newValue;
                oldUnit = newUnit;
            }

            return invalidOldValue || newValue != oldValue;
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
