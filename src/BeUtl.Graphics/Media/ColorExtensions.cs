using Beutl.Media.Immutable;

namespace Beutl.Media;

public static class ColorExtensions
{
    public static ISolidColorBrush ToBrush(this Color color)
    {
        return new SolidColorBrush(color);
    }
    
    public static ISolidColorBrush ToImmutableBrush(this Color color)
    {
        return new ImmutableSolidColorBrush(color, 1);
    }

#if false
    public static Hsv ToHsv(this Color color)
    {
        double h = default;
        double s;
        double v;
        byte min = Math.Min(Math.Min(color.R, color.G), color.B);
        byte max = Math.Max(Math.Max(color.R, color.G), color.B);

        int delta = max - min;

        v = 100.0 * max / 255.0;

        if (max == 0.0)
        {
            s = 0;
        }
        else
        {
            s = 100.0 * delta / max;
        }

        if (s == 0)
        {
            h = 0;
        }
        else
        {
            if (color.R == max)
            {
                h = 60.0 * (color.G - color.B) / delta;
            }
            else if (color.G == max)
            {
                h = 120.0 + 60.0 * (color.B - color.R) / delta;
            }
            else if (color.B == max)
            {
                h = 240.0 + 60.0 * (color.R - color.G) / delta;
            }

            if (h < 0.0)
            {
                h += 360.0;
            }
        }

        return new Hsv(h, s, v);
    }

    public static Cmyk ToCmyk(this Color color)
    {
        byte r = color.R;
        double g = color.G;
        double b = color.B;

        double rr = r / 255.0;
        double gg = g / 255.0;
        double bb = b / 255.0;

        double k = 1.0 - Math.Max(Math.Max(rr, gg), bb);
        double c = (1.0 - rr - k) / (1.0 - k);
        double m = (1.0 - gg - k) / (1.0 - k);
        double y = (1.0 - bb - k) / (1.0 - k);

        c *= 100.0;
        m *= 100.0;
        y *= 100.0;
        k *= 100.0;

        return new Cmyk(c, m, y, k);
    }

    public static YCbCr ToYCbCr(this Color color)
    {
        float r = color.R;
        float g = color.G;
        float b = color.B;

        float y = 0.299F * r + 0.587F * g + 0.114F * b;
        float cb = 128F + (-0.168736F * r - 0.331264F * g + 0.5F * b);
        float cr = 128F + (0.5F * r - 0.418688F * g - 0.081312F * b);

        return new YCbCr(y, cb, cr);
    }
#endif
}
