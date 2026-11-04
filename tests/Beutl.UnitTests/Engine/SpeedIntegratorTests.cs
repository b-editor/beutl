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
        using var integrator = new SpeedIntegrator(48000)
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
    public void Queries_in_order_sample_each_sample_once(int chunk)
    {
        const int Rate = 48000;
        const int Total = Rate * 3;
        KeyFrameAnimation<float> animation = TestAnimations.Linear(100f, 200f, MediaTime.FromSeconds(10));
        int samples = 0;
        using var integrator = new SpeedIntegrator(Rate) { SpeedTransform = value => { samples++; return value; } };
        integrator.EnsureCache(animation);

        for (int sample = 0; sample <= Total; sample += chunk)
        {
            integrator.Integrate(MediaTime.FromSamples(sample, Rate), animation);
        }

        // A query can also sample the part of a sample at its end.
        Assert.That(samples, Is.LessThanOrEqualTo(Total + Total / chunk + 1));
    }

    [Test]
    public void Queries_that_alternate_between_two_times_sample_each_sample_once()
    {
        const int Rate = 48000;
        const int Chunk = 256;
        KeyFrameAnimation<float> animation = TestAnimations.Linear(100f, 200f, MediaTime.FromSeconds(10));
        int samples = 0;
        using var integrator = new SpeedIntegrator(Rate) { SpeedTransform = value => { samples++; return value; } };
        integrator.EnsureCache(animation);
        MediaTime origin = MediaTime.FromSeconds(1.5);
        integrator.Integrate(origin, animation);
        samples = 0;

        for (int sample = 0; sample < Rate; sample += Chunk)
        {
            integrator.Integrate(origin + MediaTime.FromSamples(sample, Rate), animation);
            integrator.Integrate(origin, animation);
        }

        Assert.That(samples, Is.LessThanOrEqualTo(Rate + 2 * (Rate / Chunk + 1) + (Rate * 3 / 2)));
    }

    [Test]
    public void A_result_does_not_depend_on_what_was_asked_before()
    {
        const int Rate = 48000;
        KeyFrameAnimation<float> animation = TestAnimations.Linear(100f, 200f, MediaTime.FromSeconds(10));
        using var integrator = new SpeedIntegrator(Rate);
        integrator.EnsureCache(animation);
        for (int sample = 0; sample < Rate * 2; sample += 256)
        {
            integrator.Integrate(MediaTime.FromSamples(sample, Rate), animation);
        }

        // Forward, within a second, back within one, to an earlier second, and a fraction of a sample.
        foreach (double seconds in new[] { 2.25, 2.5, 2.1, 0.75, 1.0, 3.0, 2.123456789 })
        {
            using var fresh = new SpeedIntegrator(Rate);
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
        using var integrator = new SpeedIntegrator(Rate);
        integrator.EnsureCache(animation);
        Assert.That(integrator.Integrate(MediaTime.FromSeconds(0.5), animation).ToSeconds(), Is.EqualTo(0.5).Within(1e-6));

        ((KeyFrame<float>)animation.KeyFrames[0]).Value.Value = 200f;
        ((KeyFrame<float>)animation.KeyFrames[1]).Value.Value = 200f;

        Assert.That(integrator.Integrate(MediaTime.FromSeconds(0.75), animation).ToSeconds(), Is.EqualTo(1.5).Within(1e-6));
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

    private static void SeedIntegralCache(SpeedIntegrator integrator, int second, double integralSeconds)
    {
        // Avoid billions of integration steps while exercising the long sample-domain remainder path.
        System.Reflection.FieldInfo field = typeof(SpeedIntegrator).GetField(
            "_integralCache",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SpeedIntegrator cache field was not found.");

        field.SetValue(integrator, new Dictionary<int, double>
        {
            [second] = integralSeconds
        });
    }

    [Test]
    public void SampleRate_Setter_InvalidatesCache()
    {
        bool invalidated = false;
        using var integrator = new SpeedIntegrator(60, () => invalidated = true);
        integrator.SampleRate = 44100;
        Assert.That(invalidated, Is.True);
    }

    [Test]
    public void SampleRate_SetSameValue_DoesNotInvalidate()
    {
        bool invalidated = false;
        using var integrator = new SpeedIntegrator(60, () => invalidated = true);
        integrator.SampleRate = 60;
        Assert.That(invalidated, Is.False);
    }

    [Test]
    public void TryGetCache_BeforeInit_ReturnsMinusOne()
    {
        using var integrator = new SpeedIntegrator(60);
        var (key, value) = integrator.TryGetCache(0);
        Assert.That(key, Is.EqualTo(-1));
        Assert.That(value, Is.EqualTo(0));
    }

    [Test]
    public void Integrate_Speed100_ReturnsSameTime()
    {
        using var integrator = new SpeedIntegrator(60);
        var animation = ConstantSpeed(100f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(MediaTime.FromSeconds(2), animation);

        Assert.That(result.ToSeconds(), Is.EqualTo(2.0).Within(0.05));
    }

    [Test]
    public void Integrate_Speed200_DoublesTime()
    {
        using var integrator = new SpeedIntegrator(60);
        var animation = ConstantSpeed(200f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(MediaTime.FromSeconds(1), animation);

        Assert.That(result.ToSeconds(), Is.EqualTo(2.0).Within(0.05));
    }

    [Test]
    public void Integrate_Speed0_ReturnsZero()
    {
        using var integrator = new SpeedIntegrator(60);
        var animation = ConstantSpeed(0f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(MediaTime.FromSeconds(2), animation);

        Assert.That(result.ToSeconds(), Is.EqualTo(0).Within(0.05));
    }

    [Test]
    public void Integrate_LongTimelineRemainderUsesLongSampleIndex()
    {
        const int sampleRate = 100000;
        MediaTime start = MediaTime.FromSeconds(25200);
        MediaTime oneSample = MediaTime.FromTimeSpan(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / sampleRate));
        long startSampleIndex = 7L * 60 * 60 * sampleRate;
        double cachedIntegralSeconds = 12345.0;

        using var integrator = new SpeedIntegrator(sampleRate);
        var animation = RampedSpeedForLongTimeline();
        integrator.EnsureCache(animation);
        SeedIntegralCache(integrator, (int)start.ToSeconds(), cachedIntegralSeconds);

        MediaTime result = integrator.Integrate(start + oneSample, animation);

        Assert.Multiple(() =>
        {
            Assert.That(startSampleIndex, Is.GreaterThan((long)int.MaxValue));
            Assert.That(result.ToSeconds(), Is.EqualTo(cachedIntegralSeconds + (2.0 / sampleRate)).Within(1e-9));
        });
    }

    [Test]
    public void EnsureCache_NullAnimation_NoThrow()
    {
        using var integrator = new SpeedIntegrator(60);
        Assert.DoesNotThrow(() => integrator.EnsureCache(null));
    }

    [Test]
    public void Dispose_ClearsState()
    {
        var integrator = new SpeedIntegrator(60);
        integrator.EnsureCache(ConstantSpeed(100f));
        integrator.Dispose();

        var (key, _) = integrator.TryGetCache(0);
        Assert.That(key, Is.EqualTo(-1));
    }

    [Test]
    public void Invalidate_FiresCallback()
    {
        int callbackCount = 0;
        using var integrator = new SpeedIntegrator(60, () => callbackCount++);
        integrator.Invalidate();
        Assert.That(callbackCount, Is.EqualTo(1));
    }
}
