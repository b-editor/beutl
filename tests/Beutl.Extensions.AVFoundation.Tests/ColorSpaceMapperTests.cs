using Beutl.Extensions.AVFoundation.Interop;
using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
public class ColorSpaceMapperTests
{
    [Test]
    public void SdrSrgbReturnsCanonicalSrgbInstance()
    {
        var cs = ColorSpaceMapper.BuildColorSpace(
            isHdr: false, BeutlTransferFunction.Srgb, BeutlColorPrimaries.Srgb);
        Assert.That(cs, Is.EqualTo(BitmapColorSpace.Srgb));
    }

    [Test]
    public void HdrPqAppliesLuminanceScaling()
    {
        // PQ applies a 10000/203 gamut scale for HDR — verify the resulting gamut doesn't
        // equal the unscaled Rec.2020 one, which is how we assert the scale was applied.
        var unscaled = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Pq, BitmapColorSpaceXyz.Rec2020);
        var hdr = ColorSpaceMapper.BuildColorSpace(
            isHdr: true, BeutlTransferFunction.Pq, BeutlColorPrimaries.Rec2020);

        Assert.That(hdr, Is.Not.EqualTo(unscaled));
        Assert.That(hdr.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Pq));
    }

    [Test]
    public void HdrHlgAppliesLuminanceScaling()
    {
        var unscaled = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceTransferFn.Hlg, BitmapColorSpaceXyz.Rec2020);
        var hdr = ColorSpaceMapper.BuildColorSpace(
            isHdr: true, BeutlTransferFunction.Hlg, BeutlColorPrimaries.Rec2020);

        Assert.That(hdr, Is.Not.EqualTo(unscaled));
        Assert.That(hdr.GetNumericalTransferFunction(), Is.EqualTo(BitmapColorSpaceTransferFn.Hlg));
    }

    [Test]
    public void UnknownTagsFallBackToSrgb()
    {
        var cs = ColorSpaceMapper.BuildColorSpace(
            isHdr: false, BeutlTransferFunction.Unknown, BeutlColorPrimaries.Unknown);
        Assert.That(cs, Is.EqualTo(BitmapColorSpace.Srgb));
    }
}
