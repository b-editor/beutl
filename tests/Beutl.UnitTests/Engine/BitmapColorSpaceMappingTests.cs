using Beutl.Media;

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

    [Test]
    public void GetHdrLuminanceScale_Pq_Is10000Over203()
    {
        Assert.That(BitmapColorSpaceMapping.GetHdrLuminanceScale(BitmapColorTransfer.Pq),
            Is.EqualTo(10000f / 203f));
    }

    [Test]
    public void GetHdrLuminanceScale_Hlg_IsReciprocalOfEotfAtReferenceCode()
    {
        // Mirror the production HLG path: scale = 1 / EOTF(0.75), the BT.2100 reference code level.
        const float hlgReferenceCode = 0.75f;
        float eotf = BitmapColorSpaceTransferFn.Hlg.Transform(hlgReferenceCode);

        // The EOTF is usable (finite & positive), so the OOTF γ=3 fallback is not taken.
        Assert.That(float.IsFinite(eotf), Is.True);
        Assert.That(eotf, Is.GreaterThan(0f));

        float scale = BitmapColorSpaceMapping.GetHdrLuminanceScale(BitmapColorTransfer.Hlg);
        Assert.That(scale, Is.EqualTo(1f / eotf));
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
    public void BuildHdrColorSpace_Pq_BakesLuminanceScaleIntoGamut()
    {
        var expected = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Pq, BitmapColorSpaceXyz.Rec2020.Scale(10000f / 203f));
        var actual = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020);

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(actual.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Pq));
    }

    [Test]
    public void BuildHdrColorSpace_Pq_DiffersFromUnscaledGamut()
    {
        var unscaled = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Pq, BitmapColorSpaceXyz.Rec2020);
        var hdr = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020);
        Assert.That(hdr, Is.Not.EqualTo(unscaled));
    }

    [Test]
    public void BuildHdrColorSpace_Hlg_BakesLuminanceScaleAndKeepsTransfer()
    {
        var unscaled = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Hlg, BitmapColorSpaceXyz.Rec2020);
        var hdr = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Hlg, BitmapColorPrimaries.Rec2020);

        Assert.That(hdr, Is.Not.EqualTo(unscaled));
        Assert.That(hdr.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Hlg));
    }

    [Test]
    public void BuildHdrColorSpace_IsDeterministicAcrossCalls()
    {
        // Every backend delegates here, so two independent calls with the same tags must agree —
        // this is what guarantees the FFmpeg and AVFoundation paths produce an identical result.
        var a = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020);
        var b = BitmapColorSpaceMapping.BuildHdrColorSpace(
            BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020);
        Assert.That(a, Is.EqualTo(b));
    }
}
