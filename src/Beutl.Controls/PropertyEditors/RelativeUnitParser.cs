using Beutl.Graphics;

namespace Beutl.Controls.PropertyEditors;

internal static class RelativeUnitParser
{
    public static bool TryParse(string s, out float result, out RelativeUnit unit)
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
}
