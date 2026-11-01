using Beutl.Extensions.AVFoundation.Interop;
using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
public class ColorSpaceMapperTests
{
    // sRGB tags return the canonical sRGB instance, and unknown tags fall back to it.
    [TestCase(BitmapColorTransfer.Srgb, BitmapColorPrimaries.Srgb)]
    [TestCase(BitmapColorTransfer.Unknown, BitmapColorPrimaries.Unknown)]
    public void SdrReturnsSrgb(BitmapColorTransfer transfer, BitmapColorPrimaries primaries)
    {
        var cs = ColorSpaceMapper.BuildColorSpace(isHdr: false, transfer, primaries);
        Assert.That(cs, Is.EqualTo(BitmapColorSpace.Srgb));
    }

    // PQ and HLG keep the Rec.2020 gamut: Skia already converts both so that 1.0 is the 203 nit reference white.
    [TestCase(BitmapColorTransfer.Pq)]
    [TestCase(BitmapColorTransfer.Hlg)]
    public void HdrUsesTheRec2020Gamut(BitmapColorTransfer transfer)
    {
        var expected = BitmapColorSpace.CreateRgb(
            BitmapColorSpaceMapping.GetTransferFunction(transfer), BitmapColorSpaceXyz.Rec2020);

        var hdr = ColorSpaceMapper.BuildColorSpace(isHdr: true, transfer, BitmapColorPrimaries.Rec2020);

        Assert.That(hdr, Is.EqualTo(expected));
    }

    [Test]
    public void HdrWithUnknownPrimariesDefaultsToRec2020()
    {
        // Mirror the Swift writer's HDR-default: when a PQ/HLG stream reaches us with no
        // primaries tag, we must use the Rec.2020 gamut, not sRGB — otherwise encoder and
        // decoder pick different gamuts for the same untagged content.
        var expected = BitmapColorSpace.CreateRgb(BitmapColorSpaceTransferFn.Pq, BitmapColorSpaceXyz.Rec2020);
        var actual = ColorSpaceMapper.BuildColorSpace(
            isHdr: true, BitmapColorTransfer.Pq, BitmapColorPrimaries.Unknown);
        Assert.That(actual, Is.EqualTo(expected));
    }
}
