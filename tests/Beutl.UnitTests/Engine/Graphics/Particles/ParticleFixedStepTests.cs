using Beutl.Graphics.Particles;

namespace Beutl.UnitTests.Engine.Graphics.Particles;

[TestFixture]
public sealed class ParticleFixedStepTests
{
    // Each tick-truncated frame timestamp resolves to the step of the canonical 60 Hz timeline it falls in,
    // floor(frame * 60 * denominator / numerator), over ten minutes of frames. At 60001/1000 fps frames land
    // ~1.7e-5 steps before each integer step - a genuine offset the tick-truncation tolerance (8e-6) must not
    // swallow, or consecutive frames duplicate steps.
    [TestCase(30L, 1L, 30 * 600, TestName = "TickTruncated30FpsTimestamps_AdvanceCanonicalStepsWithoutStartupStutter")]
    [TestCase(60L, 1L, 60 * 600, TestName = "TickTruncated60FpsTimestamps_AdvanceCanonicalStepsWithoutStartupStutter")]
    [TestCase(24L, 1L, 24 * 600, TestName = "TickTruncated24FpsTimestamps_FollowTheExactSixtyHertzFloor")]
    [TestCase(120L, 1L, 120 * 600, TestName = "TickTruncated120FpsTimestamps_FollowTheExactSixtyHertzFloor")]
    [TestCase(30_000L, 1_001L, 17_983, TestName = "TickTruncatedNtsc30FpsTimestamps_FollowTheExactSixtyHertzFloor")]
    [TestCase(60_000L, 1_001L, 35_965, TestName = "TickTruncatedNtsc60FpsTimestamps_FollowTheExactSixtyHertzFloor")]
    [TestCase(60_001L, 1_000L, 240, TestName = "OffSixtyRationalRate_KeepsGenuineNearBoundaryTimestampsUnsnapped")]
    public void TickTruncatedFrameTimestamps_FollowTheExactSixtyHertzFloor(
        long numerator,
        long denominator,
        int frames)
    {
        for (int frame = 0; frame <= frames; frame++)
        {
            long ticks = frame * denominator * TimeSpan.TicksPerSecond / numerator;
            double time = TimeSpan.FromTicks(ticks).TotalSeconds;
            int actual = ParticleSimulator.ResolveTargetStep(time);
            int expected = checked((int)(frame * 60 * denominator / numerator));
            Assert.That(
                actual,
                Is.EqualTo(expected),
                $"tick-truncated frame {frame} at {numerator}/{denominator} fps "
                + "must resolve against the canonical 60 Hz timeline");
        }
    }
}
