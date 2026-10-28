using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media;

public class VideoColorDefaultsTests
{
    [TestCase(VideoColorTarget.YCbCr, BitmapColorTransfer.Bt709, VideoColorMatrix.Bt709, VideoColorRange.Limited)]
    [TestCase(VideoColorTarget.FullRangeYCbCr, BitmapColorTransfer.Bt709, VideoColorMatrix.Bt709, VideoColorRange.Full)]
    [TestCase(VideoColorTarget.Jpeg, BitmapColorTransfer.Srgb, VideoColorMatrix.Bt601, VideoColorRange.Full)]
    [TestCase(VideoColorTarget.Rgb, BitmapColorTransfer.Srgb, VideoColorMatrix.Identity, VideoColorRange.Full)]
    public void Get_ResolvesSdrDefaultsByTarget(
        VideoColorTarget target, BitmapColorTransfer transfer, VideoColorMatrix matrix, VideoColorRange range)
    {
        Assert.That(VideoColorDefaults.Get(target, hdr: false, recordsColor: true),
            Is.EqualTo(new VideoColorTags(transfer, BitmapColorPrimaries.Bt709, matrix, range)));
    }

    [TestCase(VideoColorTarget.YCbCr, VideoColorMatrix.Bt2020, VideoColorRange.Limited)]
    [TestCase(VideoColorTarget.Rgb, VideoColorMatrix.Identity, VideoColorRange.Full)]
    public void Get_UsesRec2020ForHdrEvenWhereNothingRecordsTheTags(
        VideoColorTarget target, VideoColorMatrix matrix, VideoColorRange range)
    {
        VideoColorTags tags = VideoColorDefaults.Get(target, hdr: true, recordsColor: false);

        Assert.Multiple(() =>
        {
            Assert.That(tags.Primaries, Is.EqualTo(BitmapColorPrimaries.Rec2020));
            Assert.That(tags.Matrix, Is.EqualTo(matrix));
            Assert.That(tags.Range, Is.EqualTo(range));
        });
    }

    [TestCase(VideoColorTarget.YCbCr)]
    [TestCase(VideoColorTarget.Jpeg)]
    public void Get_LeavesSdrUntaggedWhereNothingRecordsTheTags(VideoColorTarget target)
    {
        Assert.That(VideoColorDefaults.Get(target, hdr: false, recordsColor: false), Is.EqualTo(default(VideoColorTags)));
    }
}
