using Beutl.Configuration;
using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor;

public class OnionSkinTests
{
    [Test]
    public void EditorConfig_DefaultValues_AreSafe()
    {
        var config = new EditorConfig();

        Assert.That(config.IsOnionSkinEnabled, Is.False);
        Assert.That(config.OnionSkinPrevCount, Is.EqualTo(1));
        Assert.That(config.OnionSkinNextCount, Is.EqualTo(0));
        Assert.That(config.OnionSkinPrevOpacity, Is.EqualTo(0.35f).Within(0.0001f));
        Assert.That(config.OnionSkinNextOpacity, Is.EqualTo(0.35f).Within(0.0001f));
    }

    [Test]
    public void EditorConfig_SetValues_RoundTripThroughCoreProperty()
    {
        var config = new EditorConfig
        {
            IsOnionSkinEnabled = true,
            OnionSkinPrevCount = 3,
            OnionSkinNextCount = 2,
            OnionSkinPrevOpacity = 0.6f,
            OnionSkinNextOpacity = 0.2f,
        };

        Assert.That(config.IsOnionSkinEnabled, Is.True);
        Assert.That(config.OnionSkinPrevCount, Is.EqualTo(3));
        Assert.That(config.OnionSkinNextCount, Is.EqualTo(2));
        Assert.That(config.OnionSkinPrevOpacity, Is.EqualTo(0.6f).Within(0.0001f));
        Assert.That(config.OnionSkinNextOpacity, Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void EnumerateOnionSkinTimes_ZeroCounts_ReturnsEmpty()
    {
        var result = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: TimeSpan.FromSeconds(10),
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: 30,
            prevCount: 0,
            nextCount: 0,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EnumerateOnionSkinTimes_NonPositiveRate_ReturnsEmpty()
    {
        var result = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: TimeSpan.FromSeconds(10),
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: 0,
            prevCount: 2,
            nextCount: 2,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EnumerateOnionSkinTimes_NegativeCounts_TreatedAsZero()
    {
        var result = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: TimeSpan.FromSeconds(10),
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: 30,
            prevCount: -2,
            nextCount: -1,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EnumerateOnionSkinTimes_PrevAndNext_ProducesExpectedShape()
    {
        TimeSpan current = TimeSpan.FromSeconds(10);
        int rate = 30;
        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: current,
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: rate,
            prevCount: 2,
            nextCount: 1,
            prevBaseOpacity: 0.4f,
            nextBaseOpacity: 0.4f);

        // 2 prev (oldest, closest) + 1 next (closest) = 3 samples
        Assert.That(samples, Has.Count.EqualTo(3));

        // Order: prev_oldest, prev_closest, next_closest
        Assert.That(samples[0].IsPrev, Is.True);
        Assert.That(samples[0].Time, Is.EqualTo(current - TimeSpan.FromTicks(tick.Ticks * 2)));

        Assert.That(samples[1].IsPrev, Is.True);
        Assert.That(samples[1].Time, Is.EqualTo(current - tick));

        Assert.That(samples[2].IsPrev, Is.False);
        Assert.That(samples[2].Time, Is.EqualTo(current + tick));

        // Closest prev has full base opacity, oldest prev is dimmer.
        Assert.That(samples[1].Alpha, Is.GreaterThan(samples[0].Alpha));
        Assert.That(samples[1].Alpha, Is.EqualTo(0.4f).Within(0.0001f));
    }

    [Test]
    public void EnumerateOnionSkinTimes_SinglePrev_UsesFullBaseOpacity()
    {
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: TimeSpan.FromSeconds(10),
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: 30,
            prevCount: 1,
            nextCount: 0,
            prevBaseOpacity: 0.7f,
            nextBaseOpacity: 0f);

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].Alpha, Is.EqualTo(0.7f).Within(0.0001f));
    }

    [Test]
    public void EnumerateOnionSkinTimes_NearSceneStart_ClampsPrev()
    {
        int rate = 30;
        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: tick, // only one tick after start; prev=2 would underflow
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: rate,
            prevCount: 2,
            nextCount: 0,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        // Only the i=1 prev sample (at sceneStart) survives the clamp.
        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].IsPrev, Is.True);
        Assert.That(samples[0].Time, Is.EqualTo(TimeSpan.Zero));
        // Re-normalized: with only one emitted sample, alpha == baseOpacity (no falloff jump).
        Assert.That(samples[0].Alpha, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void EnumerateOnionSkinTimes_PartialPrevClamp_RenormalizesAlpha()
    {
        int rate = 30;
        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);

        // current = 2*tick, prevCount=3 → i=3 underflows, i=1 and i=2 survive.
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: TimeSpan.FromTicks(tick.Ticks * 2),
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: rate,
            prevCount: 3,
            nextCount: 0,
            prevBaseOpacity: 0.8f,
            nextBaseOpacity: 0f);

        Assert.That(samples, Has.Count.EqualTo(2));
        // Closest (last) sample should land on full baseOpacity, not (3-1+1)/3 = 0.667.
        Assert.That(samples[^1].Alpha, Is.EqualTo(0.8f).Within(0.0001f));
        // Oldest sample dims linearly with the emitted count, not the requested count.
        Assert.That(samples[0].Alpha, Is.EqualTo(0.8f * 0.5f).Within(0.0001f));
    }

    [Test]
    public void EnumerateOnionSkinTimes_CurrentAtSceneStart_DropsPrev()
    {
        int rate = 30;
        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: TimeSpan.Zero, // exactly sceneStart
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromSeconds(1),
            frameRate: rate,
            prevCount: 2,
            nextCount: 1,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        // No prev (would underflow), one next at +tick.
        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].IsPrev, Is.False);
        Assert.That(samples[0].Time, Is.EqualTo(tick));
    }

    [Test]
    public void EnumerateOnionSkinTimes_CurrentBeforeSceneStart_DropsOutOfRangeSamples()
    {
        int rate = 30;
        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);

        // current sits before sceneStart; next sample at current+tick is also before start.
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: -TimeSpan.FromTicks(tick.Ticks * 2),
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromSeconds(1),
            frameRate: rate,
            prevCount: 1,
            nextCount: 1,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        // Both prev and the +tick next remain outside [0, 1s); only "next" samples could land
        // inside if we requested enough — here we requested nextCount=1 so the single emitted
        // next sample sits at -tick which is still outside, so result is empty.
        Assert.That(samples, Is.Empty);
    }

    [Test]
    public void EnumerateOnionSkinTimes_NearSceneEnd_ClampsNext()
    {
        int rate = 30;
        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
        TimeSpan duration = TimeSpan.FromSeconds(2);
        // current = duration - 2*tick → next=3 means i=1 (ok), i=2 (== duration, excluded), i=3 (over)
        TimeSpan current = duration - TimeSpan.FromTicks(tick.Ticks * 2);

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: current,
            sceneStart: TimeSpan.Zero,
            sceneDuration: duration,
            frameRate: rate,
            prevCount: 0,
            nextCount: 3,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].IsPrev, Is.False);
        Assert.That(samples[0].Time, Is.EqualTo(current + tick));
    }

    [Test]
    public void EnumerateOnionSkinTimes_AlphaIsClampedAndMonotonic()
    {
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            current: TimeSpan.FromSeconds(10),
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: 30,
            prevCount: 4,
            nextCount: 0,
            prevBaseOpacity: 0.8f,
            nextBaseOpacity: 0f);

        // Returned order is oldest→closest, so alpha should be non-decreasing.
        for (int i = 1; i < samples.Count; i++)
        {
            Assert.That(samples[i].Alpha, Is.GreaterThanOrEqualTo(samples[i - 1].Alpha));
            Assert.That(samples[i].Alpha, Is.InRange(0f, 1f));
        }

        // Closest prev frame uses the full base opacity.
        Assert.That(samples[^1].Alpha, Is.EqualTo(0.8f).Within(0.0001f));
    }
}
