namespace Beutl.Media;

public readonly struct Cmyk : IEquatable<Cmyk>
{
    // 0-1
    public Cmyk(float c, float m, float y, float k, float a)
    {
        C = c;
        M = m;
        Y = y;
        K = k;
        A = a;
    }

    public Cmyk(Color rgb)
    {
        this = rgb.ToCmyk();
    }

    public Cmyk(Hsv hsv)
    {
        this = hsv.ToCmyk();
    }

    public float C { get; }

    public float M { get; }

    public float Y { get; }

    public float K { get; }
    
    public float A { get; }

    public static bool operator ==(Cmyk left, Cmyk right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Cmyk left, Cmyk right)
    {
        return !(left == right);
    }

    public Color ToColor()
    {
        float c = C;
        float m = M;
        float y = Y;
        float k = K;

        float r = (1.0F - c) * (1.0F - k);
        float g = (1.0F - m) * (1.0F - k);
        float b = (1.0F - y) * (1.0F - k);
        float a = A;
        r = MathF.Round(r * 255.0F);
        g = MathF.Round(g * 255.0F);
        b = MathF.Round(b * 255.0F);
        a = MathF.Round(a * 255.0F);

        return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
    }

    public Hsv ToHsv()
    {
        return ToColor().ToHsv();
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
               K == other.K &&
               A == other.A;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(C, M, Y, K, A);
    }
}
