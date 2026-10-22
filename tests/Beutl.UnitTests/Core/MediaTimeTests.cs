using System.Reflection;

namespace Beutl.UnitTests.Core;

public class MediaTimeTests
{
    private static readonly MidpointRounding[] s_roundings = Enum.GetValues<MidpointRounding>();

    private static IEnumerable<TestCaseData> ListedFrameRates()
    {
        yield return new TestCaseData(new Rational(24000, 1001)).SetArgDisplayNames("23.976");
        yield return new TestCaseData(new Rational(24)).SetArgDisplayNames("24");
        yield return new TestCaseData(new Rational(25)).SetArgDisplayNames("25");
        yield return new TestCaseData(new Rational(30000, 1001)).SetArgDisplayNames("29.97");
        yield return new TestCaseData(new Rational(30)).SetArgDisplayNames("30");
        yield return new TestCaseData(new Rational(50)).SetArgDisplayNames("50");
        yield return new TestCaseData(new Rational(60000, 1001)).SetArgDisplayNames("59.94");
        yield return new TestCaseData(new Rational(60)).SetArgDisplayNames("60");
    }

    // Frames around zero, and random frames up to about five years at 60 fps.
    private static IEnumerable<long> SampleIndices(int seed)
    {
        var random = new Random(seed);
        for (long index = -2000; index <= 2000; index++)
            yield return index;
        for (int i = 0; i < 2000; i++)
            yield return random.NextInt64(-10_000_000_000, 10_000_000_000);
    }

    [TestCaseSource(nameof(ListedFrameRates))]
    public void A_frame_on_a_listed_rate_is_exact_and_converts_back_under_every_rounding(Rational rate)
    {
        foreach (long frame in SampleIndices(2885))
        {
            MediaTime time = MediaTime.FromFrames(frame, rate);
            Int128 exact = (Int128)frame * rate.Denominator * MediaTime.FlicksPerSecond;
            if ((Int128)time.Flicks * rate.Numerator != exact)
                Assert.Fail($"frame {frame} is not exact: {time}");
            foreach (MidpointRounding rounding in s_roundings)
            {
                if (time.ToFrame(rate, rounding) != frame)
                    Assert.Fail($"frame {frame} came back as {time.ToFrame(rate, rounding)} with {rounding}");
            }
        }
    }

    [TestCaseSource(nameof(ListedFrameRates))]
    public void A_time_between_frames_rounds_as_requested(Rational rate)
    {
        MediaTime start = MediaTime.FromFrames(7, rate);
        long length = (MediaTime.FromFrames(8, rate) - start).Flicks;
        Assert.That(length % 2, Is.Zero, "every listed frame has a midpoint on the flick grid");
        Assert.Multiple(() =>
        {
            AssertFrames(start + new MediaTime(1), floor: 7, ceiling: 8, toEven: 7, awayFromZero: 7);
            AssertFrames(start + new MediaTime(length / 2), floor: 7, ceiling: 8, toEven: 8, awayFromZero: 8);
            AssertFrames(start + new MediaTime(length / 2 + 1), floor: 7, ceiling: 8, toEven: 8, awayFromZero: 8);
            AssertFrames(-start - new MediaTime(1), floor: -8, ceiling: -7, toEven: -7, awayFromZero: -7);
            AssertFrames(-start - new MediaTime(length / 2), floor: -8, ceiling: -7, toEven: -8, awayFromZero: -8);
        });

        void AssertFrames(MediaTime time, long floor, long ceiling, long toEven, long awayFromZero)
        {
            long towardZero = time.Flicks < 0 ? ceiling : floor;
            Assert.That(time.ToFrame(rate, MidpointRounding.ToNegativeInfinity), Is.EqualTo(floor), $"{time}");
            Assert.That(time.ToFrame(rate, MidpointRounding.ToPositiveInfinity), Is.EqualTo(ceiling), $"{time}");
            Assert.That(time.ToFrame(rate, MidpointRounding.ToZero), Is.EqualTo(towardZero), $"{time}");
            Assert.That(time.ToFrame(rate, MidpointRounding.ToEven), Is.EqualTo(toEven), $"{time}");
            Assert.That(time.ToFrame(rate, MidpointRounding.AwayFromZero), Is.EqualTo(awayFromZero), $"{time}");
        }
    }

    [TestCase(44_100, 16_000)]
    [TestCase(48_000, 14_700)]
    [TestCase(96_000, 7_350)]
    public void A_sample_is_a_whole_number_of_flicks_and_converts_back_exactly(int sampleRate, long flicksPerSample)
    {
        foreach (long sample in SampleIndices(sampleRate))
        {
            MediaTime time = MediaTime.FromSamples(sample, sampleRate);
            if (time.Flicks != sample * flicksPerSample)
                Assert.Fail($"sample {sample} is {time}");
            foreach (MidpointRounding rounding in s_roundings)
            {
                if (time.ToSamples(sampleRate, rounding) != sample)
                    Assert.Fail($"sample {sample} came back as {time.ToSamples(sampleRate, rounding)} with {rounding}");
            }
        }

        MediaTime between = MediaTime.FromSamples(3, sampleRate) + new MediaTime(1);
        Assert.Multiple(() =>
        {
            Assert.That(between.ToSamples(sampleRate, MidpointRounding.ToNegativeInfinity), Is.EqualTo(3));
            Assert.That(between.ToSamples(sampleRate, MidpointRounding.AwayFromZero), Is.EqualTo(3));
            Assert.That(between.ToSamples(sampleRate, MidpointRounding.ToPositiveInfinity), Is.EqualTo(4));
        });
    }

    [Test]
    public void The_lengths_the_issue_names_are_exact()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MediaTime.FromFrames(1, new Rational(30000, 1001)).Flicks, Is.EqualTo(23_543_520));
            Assert.That(MediaTime.FromSamples(1, 48_000).Flicks, Is.EqualTo(14_700));
            Assert.That(MediaTime.FromFrames(30, new Rational(30)), Is.EqualTo(MediaTime.FromSamples(48_000, 48_000)));
            Assert.That(
                MediaTime.FromFrames(1, new Rational(30)).ToSamples(44_100, MidpointRounding.ToZero),
                Is.EqualTo(1_470));
        });
    }

    // Rates whose frame is not a whole number of flicks round to the nearest one, under 1.5 ns off.
    [TestCase(11, 1, false)]
    [TestCase(15_360, 1, false)]
    [TestCase(15_360, 1, true)]
    public void A_frame_on_another_rate_is_within_a_flick_and_converts_back_under_every_rounding(
        long numerator, long denominator, bool asSamples)
    {
        var rate = new Rational(numerator, denominator);
        foreach (long frame in SampleIndices(15))
        {
            MediaTime time = asSamples
                ? MediaTime.FromSamples(frame, (int)numerator)
                : MediaTime.FromFrames(frame, rate);
            Int128 offset = (Int128)time.Flicks * numerator - (Int128)frame * denominator * MediaTime.FlicksPerSecond;
            double errorNanoseconds = (double)Int128.Abs(offset) / numerator / MediaTime.FlicksPerSecond * 1e9;
            if (errorNanoseconds >= 1.5)
                Assert.Fail($"frame {frame} is {errorNanoseconds} ns off");
            foreach (MidpointRounding rounding in s_roundings)
            {
                long back = asSamples ? time.ToSamples((int)numerator, rounding) : time.ToFrame(rate, rounding);
                if (back != frame)
                    Assert.Fail($"frame {frame} came back as {back} with {rounding}");
            }
        }
    }

    [Test]
    public void Arithmetic_and_comparison_follow_the_flick_count()
    {
        var a = new MediaTime(5);
        var b = new MediaTime(-3);
        Assert.Multiple(() =>
        {
            Assert.That((a + b).Flicks, Is.EqualTo(2));
            Assert.That((a - b).Flicks, Is.EqualTo(8));
            Assert.That((-a).Flicks, Is.EqualTo(-5));
            Assert.That(b < a && a > b && b <= a && a >= b && a != b, Is.True);
            Assert.That(a == new MediaTime(5) && a.Equals((object)new MediaTime(5)), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(new MediaTime(5).GetHashCode()));
            Assert.That(new[] { a, MediaTime.Zero, b }.Order(), Is.EqualTo(new[] { b, MediaTime.Zero, a }));
            Assert.That(a.CompareTo(null), Is.Positive);
            Assert.That(MediaTime.Zero, Is.EqualTo(new MediaTime(0)));
            Assert.That(a.ToString(), Is.EqualTo("5 flicks"));
            Assert.Throws<OverflowException>(() => _ = MediaTime.MaxValue + new MediaTime(1));
            Assert.Throws<OverflowException>(() => _ = MediaTime.MinValue - new MediaTime(1));
            Assert.Throws<OverflowException>(() => _ = -MediaTime.MinValue);
            Assert.Throws<ArgumentException>(() => a.CompareTo(5L));
        });
    }

    [Test]
    public void TimeSpan_and_seconds_convert_only_explicitly_and_to_the_nearest_unit()
    {
        var random = new Random(70);
        for (int i = 0; i < 2000; i++)
        {
            var span = TimeSpan.FromTicks(random.NextInt64(-130_000_000_000_000_000, 130_000_000_000_000_000));
            if (MediaTime.FromTimeSpan(span).ToTimeSpan() != span)
                Assert.Fail($"{span.Ticks} ticks did not come back");
        }

        Assert.Multiple(() =>
        {
            Assert.That(MediaTime.FromTimeSpan(TimeSpan.FromTicks(25)).Flicks, Is.EqualTo(1_764));
            Assert.That(MediaTime.FromTimeSpan(TimeSpan.FromTicks(1)).Flicks, Is.EqualTo(71));
            Assert.That(MediaTime.FromTimeSpan(TimeSpan.FromTicks(-1)).Flicks, Is.EqualTo(-71));
            // A 30 fps frame is 333,333.3 ticks, so it lands on the nearest tick.
            Assert.That(MediaTime.FromFrames(1, new Rational(30)).ToTimeSpan().Ticks, Is.EqualTo(333_333));
            Assert.Throws<OverflowException>(() => MediaTime.FromTimeSpan(TimeSpan.MaxValue));
            Assert.Throws<OverflowException>(() => MediaTime.FromTimeSpan(TimeSpan.MinValue));

            Assert.That(MediaTime.FromSeconds(1.5).Flicks, Is.EqualTo(1_058_400_000));
            Assert.That(MediaTime.FromSamples(1, 48_000).ToSeconds(), Is.EqualTo(1 / 48_000d));
            Assert.Throws<ArgumentException>(() => MediaTime.FromSeconds(double.NaN));
            Assert.Throws<OverflowException>(() => MediaTime.FromSeconds(double.PositiveInfinity));
            Assert.Throws<OverflowException>(() => MediaTime.FromSeconds(-1e12));

            MethodInfo[] conversions = typeof(MediaTime).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name is "op_Implicit" or "op_Explicit")
                .ToArray();
            Assert.That(conversions, Is.Empty, "conversions are methods, so rounding is never silent");
        });
    }

    [Test]
    public void Rates_that_are_not_positive_and_finite_are_rejected()
    {
        Assert.Multiple(() =>
        {
            Rational[] rates = [new Rational(0, 1), new Rational(30, 0), new Rational(-30, 1), new Rational(0, 0)];
            foreach (Rational rate in rates)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => MediaTime.FromFrames(1, rate), $"{rate}");
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => MediaTime.Zero.ToFrame(rate, MidpointRounding.ToEven), $"{rate}");
            }

            Assert.That(MediaTime.FromFrames(30, new Rational(-30, -1)), Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => MediaTime.FromSamples(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MediaTime.Zero.ToSamples(-1, MidpointRounding.ToEven));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MediaTime.Zero.ToFrame(new Rational(30), (MidpointRounding)99));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MediaTime.FromFrames(1, new Rational(30)).ToFrame(new Rational(30), (MidpointRounding)99));
        });
    }
}
