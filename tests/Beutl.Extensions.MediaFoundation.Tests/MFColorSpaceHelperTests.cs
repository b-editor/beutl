using Beutl.Embedding.MediaFoundation;
using Beutl.Media;
using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

// The Beutl.Embedding.MediaFoundation assembly is built for Windows-only runtime
// dependencies (Vortice.MediaFoundation calls Win32 MF APIs). The pure-logic
// portions exercised here only need Vortice enums + Beutl.Media, but the .NET
// host still has to resolve the entire dependency graph at test load time —
// which deps.json sometimes records with a stale fileVersion on non-Windows
// dev machines. Skip on platforms where MF isn't available.
[Platform("Win")]
[TestFixture]
public class MFColorSpaceHelperTests
{
    [Test]
    public void IsHdrTransfer_ReturnsTrueForPqAndHlg()
    {
        Assert.That(MFColorSpaceHelper.IsHdrTransfer(VideoTransferFunction.Func2084), Is.True);
        Assert.That(MFColorSpaceHelper.IsHdrTransfer(VideoTransferFunction.FuncHlg), Is.True);
    }

    [Test]
    public void IsHdrTransfer_ReturnsFalseForSdrTransfers()
    {
        Assert.That(MFColorSpaceHelper.IsHdrTransfer(VideoTransferFunction.FuncSRGB), Is.False);
        Assert.That(MFColorSpaceHelper.IsHdrTransfer(VideoTransferFunction.Func709), Is.False);
        Assert.That(MFColorSpaceHelper.IsHdrTransfer(VideoTransferFunction.FuncUnknown), Is.False);
    }

    [Test]
    public void GetTransferFunction_Func10MapsToLinear()
    {
        // MFVideoTransFunc_10 is "linear / gamma 1.0", not 10-bit content — guard
        // against the misleading Vortice name re-introducing the bug.
        Assert.That(
            MFColorSpaceHelper.GetTransferFunction(VideoTransferFunction.Func10),
            Is.EqualTo(BitmapColorSpaceTransferFn.Linear));
    }

    [Test]
    public void GetTransferFunction_UnknownFallsBackToSrgb()
    {
        Assert.That(
            MFColorSpaceHelper.GetTransferFunction(VideoTransferFunction.FuncUnknown),
            Is.EqualTo(BitmapColorSpaceTransferFn.Srgb));
    }

    [Test]
    public void TryGetTransferFunction_ReturnsFalseForUnknown()
    {
        bool mapped = MFColorSpaceHelper.TryGetTransferFunction(VideoTransferFunction.FuncUnknown, out _);
        Assert.That(mapped, Is.False);
    }

    [Test]
    public void TryGetTransferFunction_ReturnsTrueForKnownValues()
    {
        Assert.That(
            MFColorSpaceHelper.TryGetTransferFunction(VideoTransferFunction.Func2084, out var fn),
            Is.True);
        Assert.That(fn, Is.EqualTo(BitmapColorSpaceTransferFn.Pq));
    }

    [Test]
    public void GetBitmapColorSpaceXyz_DciP3AndDisplayP3AreSwapped()
    {
        // Naming is deliberately mismatched: the MF VideoPrimaries names describe
        // the source primaries set; Skia's "Dcip3" name actually refers to the D65
        // (DisplayP3) variant, and "Smpte431" refers to DCI-P3's true D-cinema white.
        Assert.That(
            MFColorSpaceHelper.GetBitmapColorSpaceXyz(VideoPrimaries.DciP3),
            Is.EqualTo(BitmapColorSpaceXyz.Smpte431));
        Assert.That(
            MFColorSpaceHelper.GetBitmapColorSpaceXyz(VideoPrimaries.DisplayP3),
            Is.EqualTo(BitmapColorSpaceXyz.Dcip3));
    }

    [Test]
    public void GetBitmapColorSpaceXyz_UnknownFallsBackToSrgb()
    {
        Assert.That(
            MFColorSpaceHelper.GetBitmapColorSpaceXyz(VideoPrimaries.Unknown),
            Is.EqualTo(BitmapColorSpaceXyz.Srgb));
    }

    [Test]
    public void BuildTargetColorSpace_SrgbReturnsCanonicalSrgb()
    {
        var cs = MFColorSpaceHelper.BuildTargetColorSpace(
            VideoTransferFunction.FuncSRGB, VideoPrimaries.Bt709);
        Assert.That(cs, Is.EqualTo(BitmapColorSpace.Srgb));
    }

    [Test]
    public void BuildHdrColorSpace_PqAppliesLuminanceScaling()
    {
        var unscaled = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Pq, BitmapColorSpaceXyz.Rec2020);
        var hdr = MFColorSpaceHelper.BuildHdrColorSpace(
            VideoTransferFunction.Func2084, VideoPrimaries.Bt2020);
        Assert.That(hdr, Is.Not.EqualTo(unscaled));
        Assert.That(hdr.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Pq));
    }

    [Test]
    public void BuildHdrColorSpace_HlgAppliesLuminanceScaling()
    {
        var unscaled = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Hlg, BitmapColorSpaceXyz.Rec2020);
        var hdr = MFColorSpaceHelper.BuildHdrColorSpace(
            VideoTransferFunction.FuncHlg, VideoPrimaries.Bt2020);
        Assert.That(hdr, Is.Not.EqualTo(unscaled));
        Assert.That(hdr.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Hlg));
    }

    [Test]
    public void BuildHdrColorSpace_UnknownPrimariesDefaultsToRec2020()
    {
        // HDR10 / HLG specs mandate Rec.2020 when primaries are missing. Falling
        // back to sRGB would silently desaturate the output.
        var expected = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Pq,
            BitmapColorSpaceXyz.Rec2020.Scale(10000f / 203f));
        var actual = MFColorSpaceHelper.BuildHdrColorSpace(
            VideoTransferFunction.Func2084, VideoPrimaries.Unknown);
        Assert.That(actual, Is.EqualTo(expected));
    }
}
