using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.UnitTests.Engine.Animation;

public class EasingsTests
{
    [Test]
    [TestCase(0f, 0f)]
    [TestCase(0.25f, 0.25f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(0.75f, 0.75f)]
    [TestCase(1f, 1f)]
    public void LinearEasing_IsIdentity(float progress, float expected)
    {
        var easing = new LinearEasing();
        Assert.That(easing.Ease(progress), Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    [TestCase(0f, 0f)]
    [TestCase(0.99f, 0f)]
    [TestCase(1f, 1f)]
    public void HoldEasing_StaysAtZeroUntilEnd(float progress, float expected)
    {
        var easing = new HoldEasing();
        Assert.That(easing.Ease(progress), Is.EqualTo(expected));
    }

    private static readonly Easing[] s_easings =
    [
        new LinearEasing(),
        new HoldEasing(),
        new BackEaseIn(),
        new BackEaseInOut(),
        new BackEaseOut(),
        new BounceEaseIn(),
        new BounceEaseInOut(),
        new BounceEaseOut(),
        new CircularEaseIn(),
        new CircularEaseInOut(),
        new CircularEaseOut(),
        new CubicEaseIn(),
        new CubicEaseInOut(),
        new CubicEaseOut(),
        new ElasticEaseIn(),
        new ElasticEaseInOut(),
        new ElasticEaseOut(),
        new ExponentialEaseIn(),
        new ExponentialEaseInOut(),
        new ExponentialEaseOut(),
        new QuadraticEaseIn(),
        new QuadraticEaseInOut(),
        new QuadraticEaseOut(),
        new QuarticEaseIn(),
        new QuarticEaseInOut(),
        new QuarticEaseOut(),
        new QuinticEaseIn(),
        new QuinticEaseInOut(),
        new QuinticEaseOut(),
        new SineEaseIn(),
        new SineEaseInOut(),
        new SineEaseOut(),
    ];

    [Test, TestCaseSource(nameof(s_easings))]
    public void Easing_AtZero_IsZero(Easing easing)
    {
        Assert.That(easing.Ease(0f), Is.EqualTo(0f).Within(1e-3));
    }

    [Test, TestCaseSource(nameof(s_easings))]
    public void Easing_AtOne_IsOne(Easing easing)
    {
        Assert.That(easing.Ease(1f), Is.EqualTo(1f).Within(1e-3));
    }

    [Test]
    public void SplineEasing_DefaultsToLinear()
    {
        var easing = new SplineEasing();
        Assert.That(easing.Ease(0.5f), Is.EqualTo(0.5f).Within(1e-3));
    }

    [Test]
    public void SplineEasing_FiresChangedOnControlPointChange()
    {
        var easing = new SplineEasing();
        int count = 0;
        easing.Changed += (_, _) => count++;

        easing.X1 = 0.25f;
        easing.Y1 = 0.5f;
        easing.X2 = 0.75f;
        easing.Y2 = 0.5f;

        Assert.That(count, Is.EqualTo(4));
    }

    [Test]
    public void SplineEasing_FromKeySplineUsesProvidedSpline()
    {
        var keySpline = new KeySpline(0.25f, 0.1f, 0.25f, 1f);
        var easing = new SplineEasing(keySpline);

        Assert.That(easing.Ease(0.5f), Is.EqualTo(keySpline.GetSplineProgress(0.5f)).Within(1e-5));
    }

    [Test]
    public void SplineEasing_InvalidX1_Throws()
    {
        var easing = new SplineEasing();
        Assert.Throws<ArgumentException>(() => easing.X1 = -0.1f);
    }
}
