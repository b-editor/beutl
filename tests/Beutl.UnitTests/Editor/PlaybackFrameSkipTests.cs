using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// Tests for PlaybackFrameSkip.ResolveNextFrame. BufferedPlayer's producer loop uses it to catch up
// with the playback consumer, which displays every frame it dequeues — so a result past the consumer's
// request is a future frame on screen followed by a freeze until the clock reaches it.
[TestFixture]
public class PlaybackFrameSkipTests
{
    [Test]
    public void ResolveNextFrame_NoRequest_AdvancesOneFrame()
    {
        Assert.That(PlaybackFrameSkip.ResolveNextFrame(100, null), Is.EqualTo(101));
    }

    [TestCase(50, Description = "consumer far behind the producer")]
    [TestCase(100, Description = "consumer at the produced frame")]
    [TestCase(101, Description = "consumer at the next frame")]
    public void ResolveNextFrame_RequestNotAhead_AdvancesOneFrame(int requestedFrame)
    {
        Assert.That(PlaybackFrameSkip.ResolveNextFrame(100, requestedFrame), Is.EqualTo(101));
    }

    [TestCase(102)]
    [TestCase(160)]
    public void ResolveNextFrame_RequestAhead_LandsExactlyOnTheRequest(int requestedFrame)
    {
        Assert.That(PlaybackFrameSkip.ResolveNextFrame(100, requestedFrame), Is.EqualTo(requestedFrame));
    }

    [Test]
    public void ResolveNextFrame_RequestAhead_NeverOvershoots()
    {
        for (int produced = 0; produced < 200; produced++)
        {
            for (int requested = produced - 5; requested < produced + 90; requested++)
            {
                int next = PlaybackFrameSkip.ResolveNextFrame(produced, requested);
                Assert.That(next, Is.LessThanOrEqualTo(Math.Max(requested, produced + 1)));
                Assert.That(next, Is.GreaterThan(produced));
            }
        }
    }

    [Test]
    public void ARequestForANegativeFrameIsStillARequest()
    {
        Assert.That(PlaybackFrameSkip.ResolveNextFrame(producedFrame: -10, requestedFrame: -3),
            Is.EqualTo(-3));
    }
}
