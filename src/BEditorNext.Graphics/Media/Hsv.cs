using System.Runtime.InteropServices;

namespace BEditorNext.Media;

[StructLayout(LayoutKind.Sequential)]
public struct Hsv : IEquatable<Hsv>
{
    // Hue 0 - 360
    // Saturation 0-100%
    // Value 0-100%

    public Hsv(double h, double s, double v)
    {
        H = h;
        S = s;
        V = v;
    }

    public Hsv(Color rgb)
    {
        this = rgb.ToHsv();
    }

    public Hsv(Cmyk cmyk)
    {
        this = cmyk.ToHsv();
    }

    public Hsv(YCbCr yc)
    {
        this = yc.ToHsv();
    }

    public double H { readonly get; set; }

    public double S { readonly get; set; }

    public double V { readonly get; set; }

    public static bool operator ==(Hsv left, Hsv right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Hsv left, Hsv right)
    {
        return !(left == right);
    }

    public readonly Color ToColor()
    {
        double r;
        double g;
        double b;
        if (S == 0)
        {
            r = g = b = Math.Round(V * 2.55);
            return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
        }

        var hh = H;
        var ss = S / 100.0;
        var vv = V / 100.0;
        if (hh >= 360.0)
            hh = 0.0;
        hh /= 60.0;

        var i = (long)hh;
        var ff = hh - i;
        var p = vv * (1.0 - ss);
        var q = vv * (1.0 - ss * ff);
        var t = vv * (1.0 - ss * (1.0 - ff));

        switch ((int)i)
        {
            case 0:
                r = vv;
                g = t;
                b = p;
                break;
            case 1:
                r = q;
                g = vv;
                b = p;
                break;
            case 2:
                r = p;
                g = vv;
                b = t;
                break;
            case 3:
                r = p;
                g = q;
                b = vv;
                break;
            case 4:
                r = t;
                g = p;
                b = vv;
                break;
            default:
                r = vv;
                g = p;
                b = q;
                break;
        }

        r = Math.Round(r * 255.0);
        g = Math.Round(g * 255.0);
        b = Math.Round(b * 255.0);

        return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
    }

    public readonly Cmyk ToCmyk()
    {
        return ToColor().ToCmyk();
    }

    public readonly YCbCr ToYCbCr()
    {
        return ToColor().ToYCbCr();
    }

    public override bool Equals(object? obj)
    {
        return obj is Hsv hsv && Equals(hsv);
    }

    public bool Equals(Hsv other)
    {
        return H == other.H &&
               S == other.S &&
               V == other.V;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(H, S, V);
    }
}
