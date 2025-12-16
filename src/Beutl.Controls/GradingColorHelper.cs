using Beutl.Media;
using UnboundedHsv = (float H, float S, float V);

namespace Beutl.Controls;

public static class GradingColorHelper
{
    private const float EPSILON = 0.001f;

    public static UnboundedHsv GetUnboundedHsv(GradingColor color)
    {
        // HSVに変換する
        var r = MathF.Abs(color.R);
        var g = MathF.Abs(color.G);
        var b = MathF.Abs(color.B);
        var min = MathF.Min(r, MathF.Min(g, b));
        var max = MathF.Max(r, MathF.Max(g, b));
        var delta = max - min;

        var h = 0f;
        var s = 0f;
        var v = max;
        if (color.R < 0 || color.G < 0 || color.B < 0)
        {
            v = -v;
        }

        if (delta > EPSILON)
        {
            s = delta / max;

            if (MathF.Abs(r - max) < EPSILON)
            {
                h = ((g - b) / delta);
            }
            else if (MathF.Abs(g - max) < EPSILON)
            {
                h = (2f + (b - r) / delta);
            }
            else
            {
                h = (4f + (r - g) / delta);
            }

            h *= 60;
        }

        if (h < 0)
            h += 360;
        else if (h >= 360)
            h -= 360;

        return (h, s, v);
    }

    public static GradingColor GetColor(UnboundedHsv color)
    {
        var hue = color.H;
        var sat = color.S;
        var val = color.V;
        float r, g, b;
        r = g = b = val;

        if (hue >= 0 && sat >= EPSILON)
        {
            hue = (hue / 360f) * 6f;

            var h = (int)hue;
            var v1 = val * (1f - sat);
            var v2 = val * (1f - sat * (hue - h));
            var v3 = val * (1f - sat * (1f - (hue - h)));

            switch (h)
            {
                case 0:
                    r = val;
                    g = v3;
                    b = v1;
                    break;

                case 1:
                    r = v2;
                    g = val;
                    b = v1;
                    break;

                case 2:
                    r = v1;
                    g = val;
                    b = v3;
                    break;

                case 3:
                    r = v1;
                    g = v2;
                    b = val;
                    break;

                case 4:
                    r = v3;
                    g = v1;
                    b = val;
                    break;

                case 5:
                    r = val;
                    g = v1;
                    b = v2;
                    break;
            }
        }

        return new GradingColor(r, g, b);
    }
}
