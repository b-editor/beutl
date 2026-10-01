using Beutl.Animation;

namespace Beutl.UnitTests.Engine.Animation;

[TestFixture]
public class KeySplineTests
{
    [Test]
    public void TryParse_ShouldReturnTrueForValidString()
    {
        bool result = KeySpline.TryParse("0.1,0.2,0.3,0.4", out var keySpline);

        Assert.That(result, Is.True);
        Assert.That(keySpline, Is.Not.Null);
        Assert.That(keySpline!.ControlPointX1, Is.EqualTo(0.1f));
        Assert.That(keySpline.ControlPointY1, Is.EqualTo(0.2f));
        Assert.That(keySpline.ControlPointX2, Is.EqualTo(0.3f));
        Assert.That(keySpline.ControlPointY2, Is.EqualTo(0.4f));
    }

    [Test]
    public void TryParse_ShouldReturnFalseForInvalidString()
    {
        bool result = KeySpline.TryParse("invalid", out var keySpline);

        Assert.That(result, Is.False);
        Assert.That(keySpline, Is.Null);
    }

    [Test]
    public void Parse_ShouldThrowFormatExceptionForInvalidString()
    {
        Assert.That(() => KeySpline.Parse("invalid"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void ControlPointX1_ShouldThrowArgumentExceptionForInvalidValue()
    {
        var keySpline = new KeySpline();

        Assert.That(() => keySpline.ControlPointX1 = -0.1f, Throws.TypeOf<ArgumentException>());
        Assert.That(() => keySpline.ControlPointX1 = 1.1f, Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ControlPointX2_ShouldThrowArgumentExceptionForInvalidValue()
    {
        var keySpline = new KeySpline();

        Assert.That(() => keySpline.ControlPointX2 = -0.1f, Throws.TypeOf<ArgumentException>());
        Assert.That(() => keySpline.ControlPointX2 = 1.1f, Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetSplineProgress_ShouldReturnLinearProgressWhenNotSpecified()
    {
        var keySpline = new KeySpline();

        float progress = keySpline.GetSplineProgress(0.5f);

        Assert.That(progress, Is.EqualTo(0.5f));
    }

    [Test]
    public void GetSplineProgress_ShouldReturnBezierValueWhenSpecified()
    {
        var keySpline = new KeySpline(0.25f, 0.1f, 0.25f, 1.0f);

        float progress = keySpline.GetSplineProgress(0.5f);

        Assert.That(progress, Is.EqualTo(0.8024f).Within(0.001f));
    }

    [Test]
    public void GetSplineProgress_ConcurrentCallersReceiveTheirOwnProgress()
    {
        var keySpline = new KeySpline(1f / 3, 0f, 2f / 3, 1f);
        int mismatches = 0;

        for (int run = 0; run < 3; run++)
        {
            using var barrier = new Barrier(2);
            void Evaluate(float progress)
            {
                float expected = 3 * progress * progress - 2 * progress * progress * progress;
                barrier.SignalAndWait();
                for (int i = 0; i < 100_000; i++)
                {
                    float actual = keySpline.GetSplineProgress(progress);
                    if (!float.IsFinite(actual) || MathF.Abs(actual - expected) > 0.001f)
                    {
                        Interlocked.Increment(ref mismatches);
                    }
                }
            }

            Parallel.Invoke(() => Evaluate(0.25f), () => Evaluate(0.5f));
        }

        Assert.That(mismatches, Is.Zero);
    }

    [Test]
    public void GetSplineProgress_UsesUpdatedControlPoints()
    {
        var keySpline = new KeySpline(1f / 3, 0f, 2f / 3, 1f);

        Assert.That(keySpline.GetSplineProgress(0.25f), Is.EqualTo(0.15625f).Within(0.001f));

        keySpline.ControlPointY1 = 1f;
        Assert.That(keySpline.GetSplineProgress(0.25f), Is.EqualTo(0.578125f).Within(0.001f));

        keySpline.ControlPointY1 = 0f;
        keySpline.ControlPointX1 = 0f;
        keySpline.ControlPointX2 = 1f;
        Assert.That(keySpline.GetSplineProgress(0.25f), Is.EqualTo(0.25f));
    }

    [Test]
    public void GetSplineProgress_ConcurrentUpdatesUseCompleteCoefficients()
    {
        var keySpline = new KeySpline(1f / 3, 0f, 2f / 3, 1f);
        int mismatches = 0;

        using var barrier = new Barrier(2);
        Parallel.Invoke(
            () =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 50_000; i++)
                {
                    keySpline.ControlPointY1 = i % 2;
                }
            },
            () =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 50_000; i++)
                {
                    float actual = keySpline.GetSplineProgress(0.25f);
                    if (!float.IsFinite(actual)
                        || (MathF.Abs(actual - 0.15625f) > 0.001f
                            && MathF.Abs(actual - 0.578125f) > 0.001f))
                    {
                        Interlocked.Increment(ref mismatches);
                    }
                }
            });

        Assert.That(mismatches, Is.Zero);
        Assert.That(keySpline.GetSplineProgress(0.25f), Is.EqualTo(0.578125f).Within(0.001f));
    }

    [Test]
    public void IsValid_ShouldReturnTrueForValidControlPoints()
    {
        var keySpline = new KeySpline(0.1f, 0.2f, 0.3f, 0.4f);

        bool isValid = keySpline.IsValid();

        Assert.That(isValid, Is.True);
    }

    [Test]
    public void IsValid_ShouldReturnFalseForInvalidControlPoints()
    {
        var keySpline = new KeySpline(-0.1f, 0.2f, 1.1f, 0.4f);

        bool isValid = keySpline.IsValid();

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void GetSplineProgress_AtZero_IsZero()
    {
        var keySpline = new KeySpline(0.42f, 0f, 0.58f, 1f);
        Assert.That(keySpline.GetSplineProgress(0f), Is.EqualTo(0f).Within(1e-6));
    }

    [Test]
    public void GetSplineProgress_AtOne_IsOne()
    {
        var keySpline = new KeySpline(0.42f, 0f, 0.58f, 1f);
        Assert.That(keySpline.GetSplineProgress(1f), Is.EqualTo(1f).Within(1e-3));
    }

    [TestCase(-0.25f, 0f)]
    [TestCase(1.25f, 1f)]
    public void GetSplineProgress_OutsideUnitInterval_UsesEndpoint(float progress, float expected)
    {
        var keySpline = new KeySpline(0.42f, 0f, 0.58f, 1f);
        Assert.That(keySpline.GetSplineProgress(progress), Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    public void TryParse_NullProvider_StillSucceeds()
    {
        Assert.That(KeySpline.TryParse("0.5,0.0,0.5,1.0".AsSpan(), out var ks, provider: null), Is.True);
        Assert.That(ks!.ControlPointX1, Is.EqualTo(0.5f));
    }

    [Test]
    public void Parse_FromSpan_RoundTrips()
    {
        var ks = KeySpline.Parse("0.5,0.0,0.5,1.0".AsSpan());
        Assert.That(ks.ControlPointX1, Is.EqualTo(0.5f));
        Assert.That(ks.ControlPointY2, Is.EqualTo(1.0f));
    }

    [Test]
    public void ControlPoints_Y_AnyValue_Allowed()
    {
        var keySpline = new KeySpline();
        // Y values are not range-restricted.
        Assert.DoesNotThrow(() => keySpline.ControlPointY1 = -1f);
        Assert.DoesNotThrow(() => keySpline.ControlPointY2 = 5f);
        Assert.That(keySpline.ControlPointY1, Is.EqualTo(-1f));
        Assert.That(keySpline.ControlPointY2, Is.EqualTo(5f));
    }
}
