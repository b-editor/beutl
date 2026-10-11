using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// Playback and export queue rendered frames as RgbaF16 snapshots outside the frame cache budget, so the number that
// may wait is derived from their size instead of being fixed.
[TestFixture]
public class ReadAheadBudgetTests
{
    // The fixed lead playback had before the budget.
    private const int PlaybackMaxFrames = 120;

    [Test]
    public void SnapshotBytes_IsTheSizeOfTheSnapshotARenderTargetAllocates()
    {
        using RenderTarget target = RenderTarget.CreateNull(64, 36);
        using Bitmap snapshot = target.CreateSnapshotBitmap();

        Assert.That(ReadAheadBudget.SnapshotBytes(new PixelSize(64, 36)), Is.EqualTo(snapshot.ByteCount));
    }

    [Test]
    public void FourKFrames_StayWithinTheBudget()
    {
        long frameBytes = ReadAheadBudget.SnapshotBytes(new PixelSize(3840, 2160));

        int frames = ReadAheadBudget.FrameCount(frameBytes, PlaybackMaxFrames);

        Assert.Multiple(() =>
        {
            Assert.That(frames, Is.EqualTo(16));
            Assert.That(frames * frameBytes, Is.LessThanOrEqualTo(ReadAheadBudget.Bytes));
        });
    }

    [Test]
    public void FullHdFrames_KeepTwoSecondsOfLeadAt30Fps()
    {
        int frames = ReadAheadBudget.FrameCount(
            ReadAheadBudget.SnapshotBytes(new PixelSize(1920, 1080)), PlaybackMaxFrames);

        Assert.That(frames, Is.EqualTo(64).And.GreaterThanOrEqualTo(2 * 30));
    }

    [Test]
    public void SmallFrames_KeepTheMaximumLead()
    {
        int frames = ReadAheadBudget.FrameCount(
            ReadAheadBudget.SnapshotBytes(new PixelSize(1280, 720)), PlaybackMaxFrames);

        Assert.That(frames, Is.EqualTo(PlaybackMaxFrames));
    }

    [Test]
    public void FramesTooLargeForTheBudget_KeepTheMinimumLead()
    {
        // One 16K frame takes almost the whole budget.
        int frames = ReadAheadBudget.FrameCount(
            ReadAheadBudget.SnapshotBytes(new PixelSize(15360, 8640)), PlaybackMaxFrames);

        Assert.That(frames, Is.EqualTo(ReadAheadBudget.MinFrames));
    }

    [Test]
    public void FrameCount_NeverExceedsTheMaximum_EvenBelowTheMinimum()
    {
        int frames = ReadAheadBudget.FrameCount(ReadAheadBudget.SnapshotBytes(new PixelSize(15360, 8640)), 2);

        Assert.That(frames, Is.EqualTo(2));
    }

    [Test]
    public void FrameCount_UsesTheGivenBudget()
    {
        long frameBytes = ReadAheadBudget.SnapshotBytes(new PixelSize(320, 240));

        Assert.That(ReadAheadBudget.FrameCount(frameBytes, PlaybackMaxFrames, frameBytes * 10), Is.EqualTo(10));
    }
}
