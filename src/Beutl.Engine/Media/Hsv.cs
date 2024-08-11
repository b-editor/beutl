using System.Runtime.InteropServices;

namespace Beutl.Media;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Hsv : IEquatable<Hsv>
{
    // Hue 0 - 360
    // Saturation 0-100%
    // Value 0-100%

    public Hsv(float h, float s, float v, float a)
    {
        H = h;
        S = s;
        V = v;
        A = a;
    }

    public Hsv(Color rgb)
    {
        this = rgb.ToHsv();
    }

    public Hsv(Cmyk cmyk)
    {
        this = cmyk.ToHsv();
    }

    public float H { get; }

    public float S { get; }

    public float V { get; }

    public float A { get; }

    public static bool operator ==(Hsv left, Hsv right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Hsv left, Hsv right)
    {
        return !(left == right);
    }

    public Color ToColor()
    {
        float r;
        float g;
        float b;
        float a = A;
        if (S == 0)
        {
            r = g = b = MathF.Round(V * 2.55F);
            return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
        }

        float hh = H;
        float ss = S / 100.0F;
        float vv = V / 100.0F;
        if (hh >= 360.0F)
            hh = 0.0F;
        hh /= 60.0F;

        float i = (long)hh;
        float ff = hh - i;
        float p = vv * (1.0F - ss);
        float q = vv * (1.0F - ss * ff);
        float t = vv * (1.0F - ss * (1.0F - ff));

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

        r = MathF.Round(r * 255.0F);
        g = MathF.Round(g * 255.0F);
        b = MathF.Round(b * 255.0F);
        a = MathF.Round(a * 255.0F);

        return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
    }

    public Cmyk ToCmyk()
    {
        return ToColor().ToCmyk();
    }

    public override bool Equals(object? obj)
    {
        return obj is Hsv hsv && Equals(hsv);
    }

    public bool Equals(Hsv other)
    {
        return H == other.H &&
               S == other.S &&
               V == other.V &&
               A == other.A;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(H, S, V, A);
    }
}
