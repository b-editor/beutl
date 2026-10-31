using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media;

using Vortice.MediaFoundation;
using Vortice.Win32;

using Windows.Win32.Media.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFMediaInfoTests
{
    [Test]
    public void GetMediaInfoText_WithVideoStream_IncludesVideoDetails()
    {
        var info = new MFMediaInfo
        {
            VideoStreamIndex = 0,
            Fps = new MFRatio { Numerator = 30, Denominator = 1 },
            HnsDuration = 10_000_000,
            TotalFrameCount = 30,
            ImageFormat = new BitmapInfoHeader { Width = 1920, Height = 1080 },
            VideoFormatName = "YUY2",
        };

        string text = info.GetMediaInfoText();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("VideoStreamIndex: 0"));
            Assert.That(text, Does.Contain("FrameSize: 1920x1080"));
            Assert.That(text, Does.Contain("Fps: 30"));
            Assert.That(text, Does.Contain("TotalFrameCount: 30"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win")]
    public void ReadColorTags_KeepsTheUntaggedReadingForHdr(bool hdr)
    {
        VideoColorTags color = MFThread.Dispatcher.Invoke(() =>
        {
            using IMFMediaType type = MediaFactory.MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.TransferFunction,
                (uint)(hdr ? VideoTransferFunction.Func2084 : VideoTransferFunction.Func709)).CheckError();
            type.Set(MediaTypeAttributeKeys.VideoPrimaries,
                (uint)(hdr ? VideoPrimaries.Bt2020 : VideoPrimaries.Bt709)).CheckError();
            type.Set(MediaTypeAttributeKeys.YuvMatrix, (uint)VideoTransferMatrix.Bt709).CheckError();
            type.Set(MediaTypeAttributeKeys.VideoNominalRange, (uint)NominalRange.Range0_255).CheckError();
            return MFColorTags.Read(type);
        });

        Assert.Multiple(() =>
        {
            // The 8-bit YUY2 path cannot show HDR, so it keeps all four BT.601 limited-range sRGB defaults.
            Assert.That(color.Matrix, Is.EqualTo(hdr ? VideoColorMatrix.Unknown : VideoColorMatrix.Bt709));
            Assert.That(color.Range, Is.EqualTo(hdr ? VideoColorRange.Unknown : VideoColorRange.Full));
            Assert.That(color.Transfer, Is.EqualTo(hdr ? BitmapColorTransfer.Unknown : BitmapColorTransfer.Bt709));
            Assert.That(color.Primaries, Is.EqualTo(hdr ? BitmapColorPrimaries.Unknown : BitmapColorPrimaries.Bt709));
        });
    }
}
