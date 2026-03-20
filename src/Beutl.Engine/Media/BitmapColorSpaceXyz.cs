using SkiaSharp;

namespace Beutl.Media;

public readonly struct BitmapColorSpaceXyz : IEquatable<BitmapColorSpaceXyz>
{
    private readonly float[] _values;

    private BitmapColorSpaceXyz(float[] values)
    {
        _values = values;
    }

    public float this[int row, int col] => _values[row * 3 + col];

    public ReadOnlySpan<float> Values => _values;

    public static BitmapColorSpaceXyz Srgb { get; } = FromSK(SKColorSpaceXyz.Srgb);

    public static BitmapColorSpaceXyz AdobeRgb { get; } = FromSK(SKColorSpaceXyz.AdobeRgb);

    public static BitmapColorSpaceXyz Dcip3 { get; } = FromSK(SKColorSpaceXyz.DisplayP3);

    public static BitmapColorSpaceXyz Rec2020 { get; } = FromSK(SKColorSpaceXyz.Rec2020);

    public static BitmapColorSpaceXyz Xyz { get; } = FromSK(SKColorSpaceXyz.Xyz);

    internal SKColorSpaceXyz ToSKXyz()
    {
        var v = _values;
        return new SKColorSpaceXyz(
            v[0], v[1], v[2],
            v[3], v[4], v[5],
            v[6], v[7], v[8]);
    }

    internal static BitmapColorSpaceXyz FromSK(SKColorSpaceXyz xyz)
    {
        return new BitmapColorSpaceXyz(xyz.Values);
    }

    public BitmapColorSpaceXyz Invert()
    {
        return FromSK(ToSKXyz().Invert());
    }

    public static BitmapColorSpaceXyz Concat(BitmapColorSpaceXyz a, BitmapColorSpaceXyz b)
    {
        return FromSK(SKColorSpaceXyz.Concat(a.ToSKXyz(), b.ToSKXyz()));
    }

    public bool Equals(BitmapColorSpaceXyz other)
    {
        if (_values is null && other._values is null) return true;
        if (_values is null || other._values is null) return false;
        return _values.AsSpan().SequenceEqual(other._values);
    }

    public override bool Equals(object? obj)
    {
        return obj is BitmapColorSpaceXyz other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (_values is null) return 0;
        var hash = new HashCode();
        foreach (float v in _values)
            hash.Add(v);
        return hash.ToHashCode();
    }

    public static bool operator ==(BitmapColorSpaceXyz left, BitmapColorSpaceXyz right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BitmapColorSpaceXyz left, BitmapColorSpaceXyz right)
    {
        return !left.Equals(right);
    }
}
