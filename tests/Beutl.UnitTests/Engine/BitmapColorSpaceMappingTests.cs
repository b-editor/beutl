using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class BitmapColorSpaceMappingTests
{
    [TestCase(BitmapColorTransfer.Pq, true)]
    [TestCase(BitmapColorTransfer.Hlg, true)]
    [TestCase(BitmapColorTransfer.Srgb, false)]
    [TestCase(BitmapColorTransfer.Bt709, false)]
    [TestCase(BitmapColorTransfer.Rec2020, false)]
    [TestCase(BitmapColorTransfer.Unknown, false)]
    public void IsHdrTransfer_OnlyPqAndHlgAreHdr(BitmapColorTransfer transfer, bool expected)
    {
        Assert.That(BitmapColorSpaceMapping.IsHdrTransfer(transfer), Is.EqualTo(expected));
    }

    // Skia already converts PQ and HLG so that 1.0 is the 203 nit reference white.
    [TestCase(BitmapColorTransfer.Pq)]
    [TestCase(BitmapColorTransfer.Hlg)]
    public void GetHdrLuminanceScale_Hdr_IsOne(BitmapColorTransfer transfer)
    {
        Assert.That(BitmapColorSpaceMapping.GetHdrLuminanceScale(transfer), Is.EqualTo(1f));
    }

    // What decoders and encoders actually get from Skia: internal linear 1.0 is reference white. A scale that assumes
    // a different PQ normalization in Skia shows up here as a factor of 49.
    [TestCase(BitmapColorTransfer.Pq, 0.58069f, 1f)]
    [TestCase(BitmapColorTransfer.Pq, 0.75183f, 1000f / 203f)]
    [TestCase(BitmapColorTransfer.Hlg, 0.75f, 1f)]
    public void BuildHdrColorSpace_DecodesReferenceWhiteToOne(BitmapColorTransfer transfer, float code, float expected)
    {
        var colorSpace = BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, BitmapColorPrimaries.Rec2020);
        using var encoded = new Bitmap(1, 1, BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, colorSpace);
        ushort value = (ushort)MathF.Round(code * 65535);
        encoded.GetPixelSpan<ushort>().Fill(value);
        encoded.GetPixelSpan<ushort>()[3] = 65535;

        using var linear = encoded.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

        Assert.That((float)linear.GetPixelSpan<RgbaF16>()[0].G, Is.EqualTo(expected).Within(expected * 0.01f));
    }

    [TestCase(BitmapColorTransfer.Pq, 0.58069f)]
    [TestCase(BitmapColorTransfer.Hlg, 0.75f)]
    public void BuildHdrColorSpace_EncodesOneToReferenceWhite(BitmapColorTransfer transfer, float expectedCode)
    {
        var colorSpace = BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, BitmapColorPrimaries.Rec2020);
        using var linear = new Bitmap(1, 1, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        linear.GetPixelSpan<RgbaF16>()[0] = new RgbaF16((Half)1, (Half)1, (Half)1, (Half)1);

        using var encoded = linear.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, colorSpace);

        Assert.That(encoded.GetPixelSpan<ushort>()[1] / 65535f, Is.EqualTo(expectedCode).Within(0.003f));
    }

    [TestCase(BitmapColorTransfer.Srgb)]
    [TestCase(BitmapColorTransfer.Bt709)]
    [TestCase(BitmapColorTransfer.Unknown)]
    public void GetHdrLuminanceScale_NonHdr_IsOne(BitmapColorTransfer transfer)
    {
        Assert.That(BitmapColorSpaceMapping.GetHdrLuminanceScale(transfer), Is.EqualTo(1.0f));
    }

    [Test]
    public void GetTransferFunction_UnknownFallsBackToSrgb()
    {
        Assert.That(BitmapColorSpaceMapping.GetTransferFunction(BitmapColorTransfer.Unknown),
            Is.EqualTo(BitmapColorSpaceTransferFn.Srgb));
    }

    [TestCase(BitmapColorTransfer.Linear)]
    [TestCase(BitmapColorTransfer.Pq)]
    [TestCase(BitmapColorTransfer.Hlg)]
    [TestCase(BitmapColorTransfer.Rec2020)]
    public void GetTransferFunction_KnownTagsRoundTripThroughBuild(BitmapColorTransfer transfer)
    {
        var fn = BitmapColorSpaceMapping.GetTransferFunction(transfer);
        var built = BitmapColorSpaceMapping.BuildTargetColorSpace(transfer, BitmapColorPrimaries.Rec2020);
        Assert.That(built.GetNumericalTransferFunction(), Is.EqualTo(fn));
    }

    [Test]
    public void GetPrimaries_UnknownFallsBackToSrgb()
    {
        Assert.That(BitmapColorSpaceMapping.GetPrimaries(BitmapColorPrimaries.Unknown),
            Is.EqualTo(BitmapColorSpaceXyz.Srgb));
    }

    [Test]
    public void GetPrimaries_Rec2020IsRec2020()
    {
        Assert.That(BitmapColorSpaceMapping.GetPrimaries(BitmapColorPrimaries.Rec2020),
            Is.EqualTo(BitmapColorSpaceXyz.Rec2020));
    }

    [Test]
    public void BuildTargetColorSpace_SrgbReturnsCanonicalSrgbInstance()
    {
        var cs = BitmapColorSpaceMapping.BuildTargetColorSpace(
            BitmapColorTransfer.Srgb, BitmapColorPrimaries.Srgb);
        Assert.That(cs, Is.SameAs(BitmapColorSpace.Srgb));
    }

    [Test]
    public void BuildTargetColorSpace_LinearSrgbReturnsCanonicalLinearInstance()
    {
        var cs = BitmapColorSpaceMapping.BuildTargetColorSpace(
            BitmapColorTransfer.Linear, BitmapColorPrimaries.Srgb);
        Assert.That(cs, Is.SameAs(BitmapColorSpace.LinearSrgb));
    }

    [Test]
    public void BuildTargetColorSpace_UnknownTagsFallBackToSrgb()
    {
        var cs = BitmapColorSpaceMapping.BuildTargetColorSpace(
            BitmapColorTransfer.Unknown, BitmapColorPrimaries.Unknown);
        Assert.That(cs, Is.EqualTo(BitmapColorSpace.Srgb));
    }

    [Test]
    public void BuildHdrColorSpace_Pq_KeepsTheRec2020Gamut()
    {
        var expected = BitmapColorSpace.CreateRgb(BitmapColorSpaceTransferFn.Pq, BitmapColorSpaceXyz.Rec2020);
        var actual = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020);

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(actual.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Pq));
    }

    [Test]
    public void BuildHdrColorSpace_Hlg_KeepsTheRec2020Gamut()
    {
        var expected = BitmapColorSpace.CreateRgb(BitmapColorSpaceTransferFn.Hlg, BitmapColorSpaceXyz.Rec2020);
        var hdr = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Hlg, BitmapColorPrimaries.Rec2020);

        Assert.That(hdr, Is.EqualTo(expected));
        Assert.That(hdr.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Hlg));
    }

    [TestCase(BitmapColorTransfer.Pq)]
    [TestCase(BitmapColorTransfer.Hlg)]
    public void BuildHdrColorSpace_UnknownPrimariesDefaultsToRec2020(BitmapColorTransfer transfer)
    {
        var explicitCs = BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, BitmapColorPrimaries.Rec2020);
        var unknownCs = BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, BitmapColorPrimaries.Unknown);

        Assert.That(unknownCs, Is.EqualTo(explicitCs));
    }
}
