using SkiaSharp;

namespace Beutl.Media;

public sealed class BitmapColorSpace : IEquatable<BitmapColorSpace>
{
    private readonly SKColorSpace _skColorSpace;

    private BitmapColorSpace(SKColorSpace skColorSpace)
    {
        _skColorSpace = skColorSpace;
    }

    public static BitmapColorSpace Srgb { get; } = new(SKColorSpace.CreateSrgb());

    public static BitmapColorSpace LinearSrgb { get; } = new(SKColorSpace.CreateSrgbLinear());

    public bool IsSrgb => _skColorSpace.IsSrgb;

    public bool GammaIsLinear => _skColorSpace.GammaIsLinear;

    public bool GammaIsCloseToSrgb => _skColorSpace.GammaIsCloseToSrgb;

    internal SKColorSpace SKColorSpace => _skColorSpace;

    public static BitmapColorSpace CreateIcc(byte[] iccData)
    {
        var skCs = SKColorSpace.CreateIcc(iccData);
        return new BitmapColorSpace(skCs);
    }

    public static BitmapColorSpace CreateRgb(BitmapColorSpaceTransferFn transferFn, BitmapColorSpaceXyz toXyzD50)
    {
        var skCs = SKColorSpace.CreateRgb(transferFn.ToSKTransferFn(), toXyzD50.ToSKXyz());
        return new BitmapColorSpace(skCs);
    }

    public BitmapColorSpaceTransferFn GetNumericalTransferFunction()
    {
        var fn = _skColorSpace.GetNumericalTransferFunction();
        return BitmapColorSpaceTransferFn.FromSK(fn);
    }

    public BitmapColorSpaceXyz ToColorSpaceXyz()
    {
        _skColorSpace.ToColorSpaceXyz(out var xyz);
        return BitmapColorSpaceXyz.FromSK(xyz);
    }

    public BitmapColorSpace ToLinearGamma()
    {
        return new(_skColorSpace.ToLinearGamma());
    }

    public BitmapColorSpace ToSrgbGamma()
    {
        return new(_skColorSpace.ToSrgbGamma());
    }

    internal static BitmapColorSpace FromSKColorSpace(SKColorSpace? colorSpace)
    {
        if (colorSpace == null || colorSpace.IsSrgb)
            return Srgb;

        return new BitmapColorSpace(colorSpace);
    }

    public bool Equals(BitmapColorSpace? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SKColorSpace.Equal(_skColorSpace, other._skColorSpace);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as BitmapColorSpace);
    }

    public override int GetHashCode()
    {
        // Hash based on transferFn + xyz gamut
        var fn = _skColorSpace.GetNumericalTransferFunction();
        _skColorSpace.ToColorSpaceXyz(out var xyz);
        return HashCode.Combine(fn.G, fn.A, fn.B, xyz.Values[0], xyz.Values[1], xyz.Values[2]);
    }

    public static bool operator ==(BitmapColorSpace? left, BitmapColorSpace? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(BitmapColorSpace? left, BitmapColorSpace? right)
    {
        return !(left == right);
    }
}
