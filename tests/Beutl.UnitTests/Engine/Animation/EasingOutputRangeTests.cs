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

    private sealed class UnknownRangeEasing : Easing
    {
        public override float Ease(float progress) => progress;
    }
}
