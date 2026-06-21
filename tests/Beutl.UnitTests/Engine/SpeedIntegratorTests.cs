using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class SpeedIntegratorTests
{
    private static KeyFrameAnimation<float> ConstantSpeed(float value)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = value,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(10),
            Value = value,
            Easing = new LinearEasing()
        });
        return animation;
    }

    private static KeyFrameAnimation<float> RampedSpeedForLongTimeline()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 100f,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromHours(7),
            Value = 200f,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromHours(8),
            Value = 200f,
            Easing = new LinearEasing()
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

        var result = integrator.Integrate(TimeSpan.FromSeconds(2), animation);

        Assert.That(result.TotalSeconds, Is.EqualTo(2.0).Within(0.05));
    }

    [Test]
    public void Integrate_Speed200_DoublesTime()
    {
        using var integrator = new SpeedIntegrator(60);
        var animation = ConstantSpeed(200f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(TimeSpan.FromSeconds(1), animation);

        Assert.That(result.TotalSeconds, Is.EqualTo(2.0).Within(0.05));
    }

    [Test]
    public void Integrate_Speed0_ReturnsZero()
    {
        using var integrator = new SpeedIntegrator(60);
        var animation = ConstantSpeed(0f);
        integrator.EnsureCache(animation);

        var result = integrator.Integrate(TimeSpan.FromSeconds(2), animation);

        Assert.That(result.TotalSeconds, Is.EqualTo(0).Within(0.05));
    }

    [Test]
    public void Integrate_LongTimelineRemainderUsesLongSampleIndex()
    {
        const int sampleRate = 100000;
        TimeSpan start = TimeSpan.FromHours(7);
        TimeSpan oneSample = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / sampleRate);
        long startSampleIndex = 7L * 60 * 60 * sampleRate;
        double cachedIntegralSeconds = 12345.0;

        using var integrator = new SpeedIntegrator(sampleRate);
        var animation = RampedSpeedForLongTimeline();
        integrator.EnsureCache(animation);
        SeedIntegralCache(integrator, (int)start.TotalSeconds, cachedIntegralSeconds);

        TimeSpan result = integrator.Integrate(start + oneSample, animation);

        Assert.Multiple(() =>
        {
            Assert.That(startSampleIndex, Is.GreaterThan((long)int.MaxValue));
            Assert.That(result.TotalSeconds, Is.EqualTo(cachedIntegralSeconds + (2.0 / sampleRate)).Within(1e-9));
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
