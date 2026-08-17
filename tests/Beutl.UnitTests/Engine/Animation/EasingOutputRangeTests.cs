using Beutl.Animation.Easings;

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
