using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class SpeedIntegratorTests
{
    [Test]
    public void ConstantTail_OnLongTimelineDoesNotSampleEveryAudioFrame()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100f } });
        int samples = 0;
        using var integrator = new SpeedIntegrator()
        {
            SpeedTransform = value => { samples++; return value; }
        };
        integrator.EnsureCache(animation);

        Assert.That(integrator.Integrate(MediaTime.FromSeconds(3600), animation), Is.EqualTo(MediaTime.FromSeconds(3600)));
        Assert.That(samples, Is.LessThan(10));
    }

    // Audio asks again for each chunk, and a chunk is a small part of a second.
    [TestCase(128)]
    [TestCase(256)]
    [TestCase(960)]
    public void Queries_in_order_reuse_the_quadrature_partition(int chunk)
    {
        const int Rate = 48000;
        const int Total = Rate * 3;
        KeyFrameAnimation<float> animation = TestAnimations.Linear(100f, 200f, MediaTime.FromSeconds(10));
        int samples = 0;
        using var integrator = new SpeedIntegrator() { SpeedTransform = value => { samples++; return value; } };
        integrator.EnsureCache(animation);

        for (int sample = 0; sample <= Total; sample += chunk)
        {
            integrator.Integrate(MediaTime.FromSamples(sample, Rate), animation);
        }

        // One rule evaluates 15 nodes. The initial partition also checks the ends and caches the tails.
        Assert.That(samples, Is.LessThanOrEqualTo(21 + 15 * (Total / chunk)));
    }

    [Test]
    public void Queries_that_alternate_between_two_times_reuse_the_quadrature_partition()
    {
        const int Rate = 48000;
        const int Chunk = 256;
        KeyFrameAnimation<float> animation = TestAnimations.Linear(100f, 200f, MediaTime.FromSeconds(10));
        int samples = 0;
        using var integrator = new SpeedIntegrator() { SpeedTransform = value => { samples++; return value; } };
        integrator.EnsureCache(animation);
        MediaTime origin = MediaTime.FromSeconds(1.5);
        integrator.Integrate(origin, animation);
        samples = 0;

        for (int sample = 0; sample < Rate; sample += Chunk)
        {
            integrator.Integrate(origin + MediaTime.FromSamples(sample, Rate), animation);
            integrator.Integrate(origin, animation);
        }

        Assert.That(samples, Is.LessThanOrEqualTo(30 * (Rate / Chunk + 1)));
    }

    [Test]
    public void A_result_does_not_depend_on_what_was_asked_before()
    {
        const int Rate = 48000;
        KeyFrameAnimation<float> animation = TestAnimations.Linear(100f, 200f, MediaTime.FromSeconds(10));
        animation.KeyFrames[1].Easing.Value = new SplineEasing(0.25f, 0.8f, 0.7f, 0.1f);
        using var integrator = new SpeedIntegrator();
        integrator.EnsureCache(animation);
        for (int sample = 0; sample < Rate * 2; sample += 256)
        {
            integrator.Integrate(MediaTime.FromSamples(sample, Rate), animation);
        }

        // Forward, within a second, back within one, to an earlier second, and a fraction of a sample.
        foreach (double seconds in new[] { 2.25, 2.5, 2.1, 0.75, 1.0, 3.0, 2.123456789 })
        {
            using var fresh = new SpeedIntegrator();
            fresh.EnsureCache(animation);
            MediaTime time = MediaTime.FromSeconds(seconds);

            Assert.That(integrator.Integrate(time, animation), Is.EqualTo(fresh.Integrate(time, animation)), $"{seconds} s");
        }
    }

    [Test]
    public void An_edit_of_the_animation_forgets_where_the_last_query_stopped()
    {
        const int Rate = 48000;
        KeyFrameAnimation<float> animation = TestAnimations.Linear(100f, 100f, MediaTime.FromSeconds(10));
        using var integrator = new SpeedIntegrator();
        integrator.EnsureCache(animation);
        Assert.That(integrator.Integrate(MediaTime.FromSeconds(0.5), animation).ToSeconds(), Is.EqualTo(0.5).Within(1e-6));

        ((KeyFrame<float>)animation.KeyFrames[0]).Value.Value = 200f;
        ((KeyFrame<float>)animation.KeyFrames[1]).Value.Value = 200f;

        Assert.That(integrator.Integrate(MediaTime.FromSeconds(0.75), animation).ToSeconds(), Is.EqualTo(1.5).Within(1e-6));
    }

    [TestCase("insert")]
    [TestCase("remove")]
    [TestCase("move")]
    [TestCase("value")]
    [TestCase("replace easing")]
    [TestCase("edit spline")]
    [TestCase("global clock")]
    public void KeyframeChangesInvalidateTheCachedIntegral(string change)
    {
        var spline = new SplineEasing(0.2f, 0.2f, 0.8f, 0.8f);
        var middle = new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(2) },
            Value = { Value = 500f },
            Easing = { Value = spline }
        };
        var animation = TestAnimations.Linear(100f, 300f, MediaTime.FromSeconds(6));
        if (change != "insert") animation.KeyFrames.Insert(1, middle);

        int invalidations = 0;
        using var integrator = new SpeedIntegrator(() => invalidations++);
        integrator.EnsureCache(animation);
        MediaTime query = MediaTime.FromSeconds(3.5);
        integrator.Integrate(query, animation);
        int version = integrator.CacheVersion;
        int previousInvalidations = invalidations;

        switch (change)
        {
            case "insert": animation.KeyFrames.Insert(1, middle); break;
            case "remove": animation.KeyFrames.Remove(middle); break;
            case "move": middle.KeyTime.Value = MediaTime.FromSeconds(2.5); break;
            case "value": middle.Value.Value = 700f; break;
            case "replace easing": middle.Easing.Value = new HoldEasing(); break;
            case "edit spline": spline.Y1 = 1f; break;
            case "global clock": animation.UseGlobalClock.Value = true; break;
        }

        integrator.EnsureCache(animation);
        using var fresh = new SpeedIntegrator();
        fresh.EnsureCache(animation);
        Assert.Multiple(() =>
        {
            Assert.That(integrator.Integrate(query, animation), Is.EqualTo(fresh.Integrate(query, animation)));
            Assert.That(integrator.CacheVersion, Is.EqualTo(version + 1));
            Assert.That(invalidations, Is.EqualTo(previousInvalidations + 1));
        });
    }

    private static KeyFrameAnimation<float> ConstantSpeed(float value)
        => TestAnimations.Linear(value, value, MediaTime.FromSeconds(10));

    private static KeyFrameAnimation<float> RampedSpeedForLongTimeline()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.Zero },
            Value = { Value = 100f },
            Easing = { Value = new LinearEasing() }
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(25200) },
            Value = { Value = 200f },
            Easing = { Value = new LinearEasing() }
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(28800) },
            Value = { Value = 200f },
            Easing = { Value = new LinearEasing() }
        });
        return animation;
    }

    [Test]
    public void Integrate_Speed100_ReturnsSameTime()
    {
        using var integrator = new SpeedIntegrator();
        var animation = ConstantSpeed(100f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(MediaTime.FromSeconds(2), animation);

        Assert.That(result.ToSeconds(), Is.EqualTo(2.0).Within(0.05));
    }

    [Test]
    public void Integrate_Speed200_DoublesTime()
    {
        using var integrator = new SpeedIntegrator();
        var animation = ConstantSpeed(200f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(MediaTime.FromSeconds(1), animation);

        Assert.That(result.ToSeconds(), Is.EqualTo(2.0).Within(0.05));
    }

    [Test]
    public void Integrate_Speed0_ReturnsZero()
    {
        using var integrator = new SpeedIntegrator();
        var animation = ConstantSpeed(0f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(MediaTime.FromSeconds(2), animation);

        Assert.That(result.ToSeconds(), Is.EqualTo(0).Within(0.05));
    }

    [Test]
    public void Integrate_SteepLinearRampMatchesAnalyticArea()
    {
        var animation = TestAnimations.Linear(2400f, 400f, MediaTime.FromSeconds(2));
        using var integrator = new SpeedIntegrator();

        Assert.Multiple(() =>
        {
            Assert.That(integrator.Integrate(MediaTime.FromSeconds(1), animation).ToSeconds(), Is.EqualTo(19).Within(1e-8));
            Assert.That(integrator.Integrate(MediaTime.FromSeconds(2), animation).ToSeconds(), Is.EqualTo(28).Within(1e-8));
        });
    }

    [Test]
    public void Integrate_EasedPieceMatchesFineReference()
    {
        const int Steps = 200000;
        const double Seconds = 1.375;
        var animation = TestAnimations.Linear(130f, 420f, MediaTime.FromSeconds(2));
        animation.KeyFrames[1].Easing.Value = new SplineEasing(0.25f, 0.8f, 0.7f, 0.1f);
        double step = Seconds / Steps;
        double expected = 0;
        for (int i = 0; i < Steps; i++)
            expected += animation.Interpolate(MediaTime.FromSeconds((i + 0.5) * step)) * step / 100d;

        using var integrator = new SpeedIntegrator();

        Assert.That(integrator.Integrate(MediaTime.FromSeconds(Seconds), animation).ToSeconds(),
            Is.EqualTo(expected).Within(5e-6));
    }

    [Test]
    public void Integrate_ClampedRampIntegratesTheKink()
    {
        var animation = TestAnimations.Linear(2400f, 400f, MediaTime.FromSeconds(2));
        using var integrator = new SpeedIntegrator() { SpeedTransform = value => Math.Min(value, 1200f) };

        // 1.2 seconds at 12x, then 0.8 seconds at an average of 8x.
        Assert.That(integrator.Integrate(MediaTime.FromSeconds(2), animation).ToSeconds(), Is.EqualTo(20.8).Within(1e-6));
    }

    [Test]
    public void Integrate_LongTimelineIncludesTheFullRampAndRemainder()
    {
        MediaTime start = MediaTime.FromSeconds(25200);
        MediaTime remainder = MediaTime.FromSeconds(0.00001);
        using var integrator = new SpeedIntegrator();
        var animation = RampedSpeedForLongTimeline();

        // The seven-hour ramp averages 150%, and the portion after it holds at 200%.
        Assert.That(integrator.Integrate(start + remainder, animation), Is.EqualTo(MediaTime.FromSeconds(37800.00002)));
    }

    [Test]
    public void EnsureCache_NullAnimation_NoThrow()
    {
        using var integrator = new SpeedIntegrator();
        Assert.DoesNotThrow(() => integrator.EnsureCache(null));
    }

    [Test]
    public void Dispose_ClearsState()
    {
        KeyFrameAnimation<float> animation = ConstantSpeed(100f);
        int invalidations = 0;
        var integrator = new SpeedIntegrator(() => invalidations++);
        integrator.EnsureCache(animation);
        integrator.Dispose();
        invalidations = 0;
        animation.UseGlobalClock.Value = true;

        Assert.That(invalidations, Is.Zero);
    }

    [Test]
    public void Invalidate_FiresCallback()
    {
        int callbackCount = 0;
        using var integrator = new SpeedIntegrator(() => callbackCount++);
        integrator.Invalidate();
        Assert.That(callbackCount, Is.EqualTo(1));
    }
}
