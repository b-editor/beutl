using SkiaSharp;

namespace Beutl.Media;

public readonly struct BitmapColorSpaceTransferFn : IEquatable<BitmapColorSpaceTransferFn>
{
    public float G { get; init; }

    public float A { get; init; }

    public float B { get; init; }

    public float C { get; init; }

    public float D { get; init; }

    public float E { get; init; }

    public float F { get; init; }

    public static BitmapColorSpaceTransferFn Srgb { get; } = FromSK(SKColorSpaceTransferFn.Srgb);

    public static BitmapColorSpaceTransferFn Linear { get; } = FromSK(SKColorSpaceTransferFn.Linear);

    public static BitmapColorSpaceTransferFn TwoDotTwo { get; } = FromSK(SKColorSpaceTransferFn.TwoDotTwo);

    public static BitmapColorSpaceTransferFn Hlg { get; } = FromSK(SKColorSpaceTransferFn.Hlg);

    public static BitmapColorSpaceTransferFn Pq { get; } = FromSK(SKColorSpaceTransferFn.Pq);

    public static BitmapColorSpaceTransferFn Rec2020 { get; } = FromSK(SKColorSpaceTransferFn.Rec2020);

    /// <summary>
    /// BT.709 / BT.601 / SMPTE 170M / IEC 61966-2-4 / BT.1361 ECG
    /// </summary>
    public static BitmapColorSpaceTransferFn Bt709 { get; } = new()
    {
        G = 1f / 0.45f,
        A = 1f / 1.099f,
        B = 0.099f / 1.099f,
        C = 1f / 4.5f,
        D = 0.081f,
        E = 0f,
        F = 0f
    };

    /// <summary>
    /// ITU-R BT.470BG - Gamma 2.8
    /// </summary>
    public static BitmapColorSpaceTransferFn Gamma28 { get; } = new()
    {
        G = 2.8f, A = 1f, B = 0f, C = 0f, D = 0f, E = 0f, F = 0f
    };

    /// <summary>
    /// SMPTE 240M
    /// </summary>
    public static BitmapColorSpaceTransferFn Smpte240M { get; } = new()
    {
        G = 1f / 0.45f,
        A = 1f / 1.1115f,
        B = 0.1115f / 1.1115f,
        C = 1f / 4.0f,
        D = 0.0912f,
        E = 0f,
        F = 0f
    };

    /// <summary>
    /// SMPTE ST 428-1 (CIE XYZ)
    /// </summary>
    public static BitmapColorSpaceTransferFn Smpte428 { get; } = new()
    {
        G = 2.6f,
        A = MathF.Pow(52.37f / 48.0f, 1.0f / 2.6f),
        B = 0f,
        C = 0f,
        D = 0f,
        E = 0f,
        F = 0f
    };

    internal SKColorSpaceTransferFn ToSKTransferFn()
    {
        return new() { G = G, A = A, B = B, C = C, D = D, E = E, F = F };
    }

    internal static BitmapColorSpaceTransferFn FromSK(SKColorSpaceTransferFn fn)
    {
        return new() { G = fn.G, A = fn.A, B = fn.B, C = fn.C, D = fn.D, E = fn.E, F = fn.F };
    }

    public BitmapColorSpaceTransferFn Invert()
    {
        return FromSK(ToSKTransferFn().Invert());
    }

    public float Transform(float x)
    {
        return ToSKTransferFn().Transform(x);
    }

    public bool Equals(BitmapColorSpaceTransferFn other)
    {
        return G == other.G && A == other.A && B == other.B && C == other.C
               && D == other.D && E == other.E && F == other.F;
    }

    public override bool Equals(object? obj)
    {
        return obj is BitmapColorSpaceTransferFn other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(G, A, B, C, D, E, F);
    }

    public static bool operator ==(BitmapColorSpaceTransferFn left, BitmapColorSpaceTransferFn right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BitmapColorSpaceTransferFn left, BitmapColorSpaceTransferFn right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        if (this == Srgb) return "sRGB";
        if (this == Linear) return "Linear";
        if (this == TwoDotTwo) return "2.2";
        if (this == Hlg) return "HLG";
        if (this == Pq) return "PQ";
        if (this == Rec2020) return "Rec.2020";
        if (this == Bt709) return "BT.709";
        if (this == Gamma28) return "2.8";
        if (this == Smpte240M) return "SMPTE 240M";
        if (this == Smpte428) return "SMPTE ST 428-1";
        return $"x >= {D}: y = ({A}x + {B})^{G} + {E}, x < {D}: y = {C}x + {F}";
    }
}
