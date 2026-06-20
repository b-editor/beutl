using Beutl.Embedding.MediaFoundation.Decoding;

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

    [Test]
    public void GetMediaInfoText_WithoutVideoStream_OmitsVideoSection()
    {
        var info = new MFMediaInfo
        {
            VideoStreamIndex = -1,
            HnsDuration = 10_000_000,
            VideoFormatName = "Unknown",
        };

        string text = info.GetMediaInfoText();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Play time"));
            Assert.That(text, Does.Not.Contain("VideoStreamIndex"));
            Assert.That(text, Does.Not.Contain("FrameSize"));
        });
    }
}
