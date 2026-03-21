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
}
