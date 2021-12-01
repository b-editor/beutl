namespace BEditorNext.Graphics;

public struct Cmyk : IEquatable<Cmyk>
{
    public Cmyk(double c, double m, double y, double k)
    {
        C = c;
        M = m;
        Y = y;
        K = k;
    }

    public Cmyk(Color rgb)
    {
        this = rgb.ToCmyk();
    }

    public Cmyk(Hsv hsv)
    {
        this = hsv.ToCmyk();
    }

    public Cmyk(YCbCr yc)
    {
        this = yc.ToCmyk();
    }

    public double C { readonly get; set; }

    public double M { readonly get; set; }

    public double Y { readonly get; set; }

    public double K { readonly get; set; }

    public static bool operator ==(Cmyk left, Cmyk right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Cmyk left, Cmyk right)
    {
        return !(left == right);
    }

    public readonly Color ToColor()
    {
        var cc = C / 100.0;
        var mm = M / 100.0;
        var yy = Y / 100.0;
        var kk = K / 100.0;

        var r = (1.0 - cc) * (1.0 - kk);
        var g = (1.0 - mm) * (1.0 - kk);
        var b = (1.0 - yy) * (1.0 - kk);
        r = Math.Round(r * 255.0);
        g = Math.Round(g * 255.0);
        b = Math.Round(b * 255.0);

        return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
    }

    public readonly Hsv ToHsv()
    {
        return ToColor().ToHsv();
    }

    public readonly YCbCr ToYCbCr()
    {
        return ToColor().ToYCbCr();
    }

    public override bool Equals(object? obj)
    {
        return obj is Cmyk cmyk && Equals(cmyk);
    }

    public bool Equals(Cmyk other)
    {
        return C == other.C &&
               M == other.M &&
               Y == other.Y &&
               K == other.K;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(C, M, Y, K);
    }
}
