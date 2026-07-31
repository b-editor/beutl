using Beutl.Graphics.Particles;

namespace Beutl.UnitTests.Engine.Graphics.Particles;

[TestFixture]
public sealed class ParticleFixedStepTests
{
    [TestCase(30)]
    [TestCase(60)]
    public void FrameTimestamps_AdvanceCanonicalStepsWithoutFreezeOrCatchUp(int frameRate)
    {
        const int maximumFrame = 36_000;
        int previousStep = ParticleSimulator.ResolveTargetStep(0);
        int expectedAdvance = 60 / frameRate;

        for (int frame = 1; frame <= maximumFrame; frame++)
        {
            float time = frame / (float)frameRate;
            int step = ParticleSimulator.ResolveTargetStep(time);
            Assert.That(
                step - previousStep,
                Is.EqualTo(expectedAdvance),
                $"frame {frame} at {frameRate} fps resolved from {previousStep} to {step}");
            previousStep = step;
        }
    }

    [TestCase(30)]
    [TestCase(60)]
    public void TickTruncatedFrameTimestamps_AdvanceCanonicalStepsWithoutStartupStutter(
        int frameRate)
    {
        int maximumFrame = frameRate * 60 * 10;
        int previousStep = ParticleSimulator.ResolveTargetStep(0);
        int expectedAdvance = 60 / frameRate;

        for (int frame = 1; frame <= maximumFrame; frame++)
        {
            long ticks = (long)frame * TimeSpan.TicksPerSecond / frameRate;
            float time = (float)TimeSpan.FromTicks(ticks).TotalSeconds;
            int step = ParticleSimulator.ResolveTargetStep(time);
            Assert.That(
                step - previousStep,
                Is.EqualTo(expectedAdvance),
                $"tick-truncated frame {frame} at {frameRate} fps resolved from {previousStep} to {step}");
            previousStep = step;
        }
    }

    [TestCase(24)]
    [TestCase(120)]
    public void FractionalStepFrameRates_FollowTheExactSixtyHertzFloor(int frameRate)
    {
        int maximumFrame = frameRate * 60 * 10;

        for (int frame = 0; frame <= maximumFrame; frame++)
        {
            int actual = ParticleSimulator.ResolveTargetStep(frame / (float)frameRate);
            int expected = (int)Math.Floor(frame * 60d / frameRate);
            Assert.That(
                actual,
                Is.EqualTo(expected),
                $"frame {frame} at {frameRate} fps must resolve against the canonical 60 Hz timeline");
        }
    }

    [TestCase(24)]
    [TestCase(120)]
    public void TickTruncatedFractionalFrameRates_FollowTheExactSixtyHertzFloor(
        int frameRate)
    {
        int maximumFrame = frameRate * 60 * 10;

        for (int frame = 0; frame <= maximumFrame; frame++)
        {
            long ticks = (long)frame * TimeSpan.TicksPerSecond / frameRate;
            float time = (float)TimeSpan.FromTicks(ticks).TotalSeconds;
            int actual = ParticleSimulator.ResolveTargetStep(time);
            int expected = (int)Math.Floor(frame * 60d / frameRate);
            Assert.That(
                actual,
                Is.EqualTo(expected),
                $"tick-truncated frame {frame} at {frameRate} fps must resolve against the canonical 60 Hz timeline");
        }
    }

    [TestCase(30_000, 1_001)]
    [TestCase(60_000, 1_001)]
    public void TickTruncatedNtscFrameRates_FollowTheExactSixtyHertzFloorForTenMinutes(
        int numerator,
        int denominator)
    {
        int maximumFrame = (int)Math.Ceiling(600d * numerator / denominator);

        for (int frame = 0; frame <= maximumFrame; frame++)
        {
            long ticks = (long)frame * denominator * TimeSpan.TicksPerSecond / numerator;
            double time = TimeSpan.FromTicks(ticks).TotalSeconds;
            int actual = ParticleSimulator.ResolveTargetStep(time);
            int expected = checked((int)((long)frame * 60 * denominator / numerator));
            Assert.That(
                actual,
                Is.EqualTo(expected),
                $"tick-truncated NTSC frame {frame} at {numerator}/{denominator} fps "
                + "must resolve against the canonical 60 Hz timeline");
        }
    }
    [Test]
    public void OffSixtyRationalRate_KeepsGenuineNearBoundaryTimestampsUnsnapped()
    {
        // 60001/1000 fps frames land ~1.7e-5 steps before each integer step — a genuine offset the
        // tick-truncation tolerance (8e-6) must not swallow, or consecutive frames duplicate steps.
        const long numerator = 60001;
        const long denominator = 1000;
        int previous = ParticleSimulator.ResolveTargetStep(0d);
        for (int frame = 1; frame <= 240; frame++)
        {
            long ticks = frame * denominator * TimeSpan.TicksPerSecond / numerator;
            double time = TimeSpan.FromTicks(ticks).TotalSeconds;
            int actual = ParticleSimulator.ResolveTargetStep(time);
            int expected = (int)(frame * 60L * denominator / numerator);
            Assert.That(
                actual,
                Is.EqualTo(expected),
                $"frame {frame} at {numerator}/{denominator} fps must floor against the true timeline");
            Assert.That(actual - previous, Is.InRange(0, 1));
            previous = actual;
        }
    }
}
