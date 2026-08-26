using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Animation;

[TestFixture]
public class EasingOutputRangeTests
{
    [Test]
    public void BuiltInEasings_ReportConservativeFiniteRanges()
    {
        Type[] easingTypes = typeof(Easing).Assembly.GetTypes()
            .Where(type => type.IsPublic
                && !type.IsAbstract
                && typeof(Easing).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        Assert.That(easingTypes, Is.Not.Empty);

        foreach (Type easingType in easingTypes)
        {
            var easing = (Easing)Activator.CreateInstance(easingType)!;
            Assert.Multiple(() =>
            {
                Assert.That(
                    easing.TryGetOutputRange(out float minimum, out float maximum),
                    Is.True,
                    $"{easingType.Name} must publish a conservative range.");
                Assert.That(minimum, Is.Not.NaN, $"{easingType.Name} minimum");
                Assert.That(maximum, Is.Not.NaN, $"{easingType.Name} maximum");
                Assert.That(float.IsInfinity(minimum), Is.False, $"{easingType.Name} minimum");
                Assert.That(float.IsInfinity(maximum), Is.False, $"{easingType.Name} maximum");
                Assert.That(minimum, Is.LessThanOrEqualTo(maximum), easingType.Name);

                const int sampleCount = 10_000;
                for (int i = 0; i <= sampleCount; i++)
                {
                    float progress = i / (float)sampleCount;
                    float value = easing.Ease(progress);
                    Assert.That(
                        value,
                        Is.InRange(minimum - 1e-5f, maximum + 1e-5f),
                        $"{easingType.Name} at progress {progress}");
                }
            });
        }
    }

    [Test]
    public void SplineEasing_RangeContainsControlPointHull()
    {
        var easing = new SplineEasing(0.25f, -2f, 0.75f, 3f);

        Assert.That(easing.TryGetOutputRange(out float minimum, out float maximum), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(minimum, Is.EqualTo(-2f));
            Assert.That(maximum, Is.EqualTo(3f));
        });
    }

    [Test]
    public void CustomEasing_DefaultsToUnknownRange()
    {
        var easing = new UnknownRangeEasing();

        Assert.That(easing.TryGetOutputRange(out _, out _), Is.False);
    }

    [Test]
    public void BackEaseRanges_ContainFloatingPointEndpoints()
    {
        var easeIn = new BackEaseIn();
        var easeOut = new BackEaseOut();

        Assert.Multiple(() =>
        {
            Assert.That(easeIn.TryGetOutputRange(out _, out float easeInMaximum), Is.True);
            Assert.That(easeIn.Ease(1f), Is.LessThanOrEqualTo(easeInMaximum));
            Assert.That(easeOut.TryGetOutputRange(out float easeOutMinimum, out _), Is.True);
            Assert.That(easeOut.Ease(0f), Is.GreaterThanOrEqualTo(easeOutMinimum));
        });
    }

    [Test]
    public void BackEaseOut_PartialRangeExcludesLaterOvershoot()
    {
        var easing = new BackEaseOut();

        Assert.That(
            easing.TryGetOutputRange(0f, 0.1f, out float minimum, out float maximum),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(minimum, Is.LessThanOrEqualTo(easing.Ease(0f)));
            Assert.That(maximum, Is.GreaterThanOrEqualTo(easing.Ease(0.1f)));
            Assert.That(maximum, Is.LessThan(1f));
        });
    }

    [Test]
    public void BackEasings_PartialRangesContainDenseSamples()
    {
        Easing[] easings = [new BackEaseIn(), new BackEaseOut(), new BackEaseInOut()];
        var random = new Random(42);

        foreach (Easing easing in easings)
        {
            for (int rangeIndex = 0; rangeIndex < 100; rangeIndex++)
            {
                float first = random.NextSingle();
                float second = random.NextSingle();
                float start = Math.Min(first, second);
                float end = Math.Max(first, second);
                Assert.That(
                    easing.TryGetOutputRange(start, end, out float minimum, out float maximum),
                    Is.True);

                for (int sampleIndex = 0; sampleIndex <= 100; sampleIndex++)
                {
                    float progress = start + (end - start) * sampleIndex / 100f;
                    Assert.That(
                        easing.Ease(progress),
                        Is.InRange(minimum, maximum),
                        $"{easing.GetType().Name} at progress {progress} in [{start}, {end}]");
                }
            }
        }
    }

    [Test]
    public void PartialRange_SingletonUsesExactFiniteValue()
    {
        var easing = new BackEaseOut();

        Assert.That(easing.TryGetOutputRange(0.25f, 0.25f, out float minimum, out float maximum), Is.True);
        Assert.That(minimum, Is.EqualTo(easing.Ease(0.25f)));
        Assert.That(maximum, Is.EqualTo(minimum));
    }

    [Test]
    public void PartialRange_InvalidProgressThrows()
    {
        var easing = new BackEaseOut();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => easing.TryGetOutputRange(-0.1f, 1f, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => easing.TryGetOutputRange(0f, float.NaN, out _, out _));
            Assert.Throws<ArgumentException>(() => easing.TryGetOutputRange(0.75f, 0.25f, out _, out _));
        });
    }

    [Test]
    public void KeyFrameAnimation_PartialClockRangeExcludesLaterOvershoot()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 0f,
            Easing = new BackEaseOut(),
        });

        Assert.That(
            animation.TryGetOutputRange(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromMilliseconds(100)),
                out float minimum,
                out float maximum),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(minimum, Is.GreaterThan(0));
            Assert.That(maximum, Is.GreaterThanOrEqualTo(100));
        });
    }

    [Test]
    public void KeyFrameAnimation_PartialClockRangeIgnoresInvalidFutureState()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 100f });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = float.NaN });

        Assert.That(
            animation.TryGetOutputRange(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromMilliseconds(500)),
                out float minimum,
                out float maximum),
            Is.True);
        Assert.That(minimum, Is.EqualTo(100f));
        Assert.That(maximum, Is.EqualTo(100f));
    }

    [Test]
    public void BounceEaseRanges_ContainFloatingPointExtrema()
    {
        var easeIn = new BounceEaseIn();
        var easeOut = new BounceEaseOut();
        var easeInOut = new BounceEaseInOut();

        Assert.Multiple(() =>
        {
            Assert.That(easeIn.TryGetOutputRange(out float easeInMinimum, out float easeInMaximum), Is.True);
            Assert.That(easeOut.TryGetOutputRange(out float easeOutMinimum, out float easeOutMaximum), Is.True);
            Assert.That(easeInOut.TryGetOutputRange(out float easeInOutMinimum, out float easeInOutMaximum), Is.True);
            Assert.That(easeIn.Ease(4f / 11f), Is.InRange(easeInMinimum, easeInMaximum));
            Assert.That(easeOut.Ease(4f / 11f), Is.InRange(easeOutMinimum, easeOutMaximum));
            Assert.That(easeInOut.Ease(15f / 22f), Is.InRange(easeInOutMinimum, easeInOutMaximum));
        });
    }

    [Test]
    public void SplineEasing_RejectsRangesWhenBezierCoefficientsOverflow()
    {
        var easing = new SplineEasing(0.25f, float.MaxValue / 2f, 0.75f, 0f);

        Assert.That(easing.TryGetOutputRange(out _, out _), Is.False);
    }

    private sealed class UnknownRangeEasing : Easing
    {
        public override float Ease(float progress) => progress;
    }
}
