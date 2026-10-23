using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

public class BitmapColorSpaceTests
{
    [Test]
    public void Srgb_IsSrgb_True()
    {
        Assert.That(BitmapColorSpace.Srgb.IsSrgb, Is.True);
    }

    [Test]
    public void LinearSrgb_GammaIsLinear()
    {
        Assert.That(BitmapColorSpace.LinearSrgb.GammaIsLinear, Is.True);
    }

    [Test]
    public void Equality_SrgbVsLinear_AreNotEqual()
    {
        Assert.That(BitmapColorSpace.Srgb, Is.Not.EqualTo(BitmapColorSpace.LinearSrgb));
        Assert.That(BitmapColorSpace.Srgb != BitmapColorSpace.LinearSrgb, Is.True);
    }

    [Test]
    public void Equals_AgainstNull_ReturnsFalse()
    {
        BitmapColorSpace srgb = BitmapColorSpace.Srgb;
        Assert.That(srgb!.Equals((BitmapColorSpace?)null), Is.False);
        Assert.That(srgb!.Equals((object?)null), Is.False);
    }

    [Test]
    public void ToLinearGamma_IsEquivalentToLinearSrgb()
    {
        var converted = BitmapColorSpace.Srgb.ToLinearGamma();
        Assert.That(converted.GammaIsLinear, Is.True);
    }

    [Test]
    public void ToSrgbGamma_IsEquivalentToSrgb()
    {
        var converted = BitmapColorSpace.LinearSrgb.ToSrgbGamma();
        Assert.That(converted.IsSrgb, Is.True);
    }

    [Test]
    public void ToString_ReturnsNonEmpty()
    {
        Assert.That(BitmapColorSpace.Srgb.ToString(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void OperatorEquality_BothNull_True()
    {
        BitmapColorSpace? a = null;
        BitmapColorSpace? b = null;
        Assert.That(a == b, Is.True);
        Assert.That(a != b, Is.False);
    }

    [Test]
    public void OperatorEquality_LeftNull_RightNotNull_False()
    {
        BitmapColorSpace? a = null;
        Assert.That(a == BitmapColorSpace.Srgb, Is.False);
    }

    [Test]
    public void CreateIcc_InvalidData_ReturnsNull()
    {
        Assert.That(BitmapColorSpace.CreateIcc([0x00, 0x01, 0x02]), Is.Null);
    }

    [Test]
    public void CreateIcc_RgbMatrixProfile_ReturnsAColorSpace()
    {
        BitmapColorSpace? colorSpace = BitmapColorSpace.CreateIcc(CreateRgbProfile());

        Assert.That(colorSpace, Is.Not.Null);
        Assert.That(colorSpace!.IsSrgb, Is.False);
    }

    // Skia reads a CMYK profile but cannot make a color space of it, so callers fall back to their own.
    [Test]
    public void CreateIcc_CmykProfile_ReturnsNull()
    {
        byte[] profile = CreateCmykProfile();
        using (SKColorSpaceIccProfile? parsed = SKColorSpaceIccProfile.Create(profile))
            Assert.That(parsed, Is.Not.Null, "Skia must read the profile for this to reach the color-space check.");

        Assert.That(BitmapColorSpace.CreateIcc(profile), Is.Null);
    }

    // sRGB's D50 primaries with a gamma of 2.2.
    private static byte[] CreateRgbProfile()
    {
        byte[] gamma = [.. "curv"u8, 0, 0, 0, 0, 0, 0, 0, 1, 0x02, 0x33];
        return CreateProfile("mntr"u8, "RGB "u8, "XYZ "u8,
        [
            ("rXYZ", Xyz(0.4360747f, 0.2225045f, 0.0139322f)),
            ("gXYZ", Xyz(0.3850649f, 0.7168786f, 0.0971045f)),
            ("bXYZ", Xyz(0.1430804f, 0.0606169f, 0.7141733f)),
            ("rTRC", gamma),
            ("gTRC", gamma),
            ("bTRC", gamma),
        ]);
    }

    // A CMYK-to-Lab lut8 with identity curves and a two-point grid.
    private static byte[] CreateCmykProfile()
    {
        byte[] identity = [.. Enumerable.Range(0, 256).Select(static value => (byte)value)];
        byte[] lut =
        [
            .. "mft1"u8, 0, 0, 0, 0, 4, 3, 2, 0,
            .. Fixed(1), .. Fixed(0), .. Fixed(0), .. Fixed(0), .. Fixed(1), .. Fixed(0), .. Fixed(0), .. Fixed(0), .. Fixed(1),
            .. identity, .. identity, .. identity, .. identity,
            .. new byte[16 * 3],
            .. identity, .. identity, .. identity,
        ];
        return CreateProfile("prtr"u8, "CMYK"u8, "Lab "u8, [("A2B0", lut)]);
    }

    private static byte[] CreateProfile(
        ReadOnlySpan<byte> deviceClass, ReadOnlySpan<byte> colorSpace, ReadOnlySpan<byte> pcs,
        (string Signature, byte[] Data)[] tags)
    {
        int offset = 128 + 4 + (12 * tags.Length);
        var header = new byte[128];
        var table = new List<byte>(BigEndian(tags.Length));
        var data = new List<byte>();
        foreach ((string signature, byte[] tagData) in tags)
        {
            table.AddRange([.. System.Text.Encoding.ASCII.GetBytes(signature), .. BigEndian(offset + data.Count), .. BigEndian(tagData.Length)]);
            data.AddRange(tagData);
        }

        BigEndian(offset + data.Count).CopyTo(header, 0);
        BigEndian(0x02100000).CopyTo(header, 8);
        deviceClass.CopyTo(header.AsSpan(12));
        colorSpace.CopyTo(header.AsSpan(16));
        pcs.CopyTo(header.AsSpan(20));
        "acsp"u8.CopyTo(header.AsSpan(36));
        Xyz(0.9642f, 1f, 0.8249f).AsSpan(8).CopyTo(header.AsSpan(68));
        return [.. header, .. table, .. data];
    }

    private static byte[] Xyz(float x, float y, float z) => [.. "XYZ "u8, 0, 0, 0, 0, .. Fixed(x), .. Fixed(y), .. Fixed(z)];

    private static byte[] Fixed(float value) => BigEndian((int)MathF.Round(value * 65536));

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
