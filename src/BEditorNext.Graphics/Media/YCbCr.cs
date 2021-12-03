using System.Runtime.InteropServices;

namespace BEditorNext.Media;

[StructLayout(LayoutKind.Sequential)]
public struct YCbCr : IEquatable<YCbCr>
{
    public YCbCr(float y, float cb, float cr)
    {
        Y = y;
        Cb = cb;
        Cr = cr;
    }

    public YCbCr(Color rgb)
    {
        this = rgb.ToYCbCr();
    }

    public YCbCr(Hsv hsv)
    {
        this = hsv.ToYCbCr();
    }

    public YCbCr(Cmyk cmyk)
    {
        this = cmyk.ToYCbCr();
    }

    public float Y { readonly get; set; }

    public float Cb { readonly get; set; }

    public float Cr { readonly get; set; }

    public static bool operator ==(YCbCr left, YCbCr right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(YCbCr left, YCbCr right)
    {
        return !(left == right);
    }

    public readonly Color ToColor()
    {
        var y = Y;
        var cb = Cb - 128F;
        var cr = Cr - 128F;

        var r = MathF.Round(y + 1.402F * cr, MidpointRounding.AwayFromZero);
        var g = MathF.Round(y - 0.344136F * cb - 0.714136F * cr, MidpointRounding.AwayFromZero);
        var b = MathF.Round(y + 1.772F * cb, MidpointRounding.AwayFromZero);

        return Color.FromArgb(255, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    public readonly Cmyk ToCmyk()
    {
        return ToColor().ToCmyk();
    }

    public readonly Hsv ToHsv()
    {
        return ToColor().ToHsv();
    }

    public override bool Equals(object? obj)
    {
        return obj is YCbCr cr && Equals(cr);
    }

    public bool Equals(YCbCr other)
    {
        return Y == other.Y &&
               Cb == other.Cb &&
               Cr == other.Cr;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Y, Cb, Cr);
    }
}
