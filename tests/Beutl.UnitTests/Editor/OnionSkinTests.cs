using Beutl.Configuration;
using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor;

public class OnionSkinTests
{
    // Mirror of the shared frame<->time conversion (int.ToTimeSpan(rate)) so expected sample
    // times are expressed on the same frame grid the helper now derives them from.
    private static TimeSpan FrameTime(int frame, int rate) =>
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond * frame / rate);

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
            currentFrame: 300,
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
            currentFrame: 300,
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
            currentFrame: 300,
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
        int rate = 30;
        int currentFrame = 300;

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: currentFrame,
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
        Assert.That(samples[0].Time, Is.EqualTo(FrameTime(currentFrame - 2, rate)));

        Assert.That(samples[1].IsPrev, Is.True);
        Assert.That(samples[1].Time, Is.EqualTo(FrameTime(currentFrame - 1, rate)));

        Assert.That(samples[2].IsPrev, Is.False);
        Assert.That(samples[2].Time, Is.EqualTo(FrameTime(currentFrame + 1, rate)));

        // Closest prev has full base opacity, oldest prev is dimmer.
        Assert.That(samples[1].Alpha, Is.GreaterThan(samples[0].Alpha));
        Assert.That(samples[1].Alpha, Is.EqualTo(0.4f).Within(0.0001f));
    }

    [Test]
    public void EnumerateOnionSkinTimes_SinglePrev_UsesFullBaseOpacity()
    {
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: 300,
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

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: 1, // only one frame after start; prev=2 would underflow
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: rate,
            prevCount: 2,
            nextCount: 0,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        // Only the i=1 prev sample (frame 0, at sceneStart) survives the clamp.
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

        // currentFrame = 2, prevCount=3 → i=3 underflows (frame -1), i=1 and i=2 survive.
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: 2,
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

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: 0, // exactly sceneStart
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromSeconds(1),
            frameRate: rate,
            prevCount: 2,
            nextCount: 1,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        // No prev (would underflow), one next at frame 1.
        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].IsPrev, Is.False);
        Assert.That(samples[0].Time, Is.EqualTo(FrameTime(1, rate)));
    }

    [Test]
    public void EnumerateOnionSkinTimes_CurrentBeforeSceneStart_DropsOutOfRangeSamples()
    {
        int rate = 30;

        // current sits before sceneStart; the single next sample (frame -1) is also before start.
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: -2,
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromSeconds(1),
            frameRate: rate,
            prevCount: 1,
            nextCount: 1,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        // prev (frame -3) and the single next (frame -1) both remain outside [0, 1s), so empty.
        Assert.That(samples, Is.Empty);
    }

    [Test]
    public void EnumerateOnionSkinTimes_NearSceneEnd_ClampsNext()
    {
        int rate = 30;
        TimeSpan duration = TimeSpan.FromSeconds(2); // exactly 60 frames at 30 fps
        // currentFrame = 58 → next=3 means frame 59 (ok), frame 60 (== duration, excluded), frame 61 (over)
        int currentFrame = 58;

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: currentFrame,
            sceneStart: TimeSpan.Zero,
            sceneDuration: duration,
            frameRate: rate,
            prevCount: 0,
            nextCount: 3,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0.5f);

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].IsPrev, Is.False);
        Assert.That(samples[0].Time, Is.EqualTo(FrameTime(currentFrame + 1, rate)));
    }

    [Test]
    public void EnumerateOnionSkinTimes_AlphaIsClampedAndMonotonic()
    {
        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: 300,
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

    [Test]
    public void EnumerateOnionSkinTimes_NextOnly_ClosestFrameUsesFullBaseOpacity()
    {
        int rate = 30;
        int currentFrame = 300;

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: currentFrame,
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: rate,
            prevCount: 0,
            nextCount: 2,
            prevBaseOpacity: 0f,
            nextBaseOpacity: 0.4f);

        // 2 next samples, ordered closest → farthest.
        Assert.That(samples, Has.Count.EqualTo(2));

        Assert.That(samples[0].IsPrev, Is.False);
        Assert.That(samples[0].Time, Is.EqualTo(FrameTime(currentFrame + 1, rate)));
        Assert.That(samples[1].IsPrev, Is.False);
        Assert.That(samples[1].Time, Is.EqualTo(FrameTime(currentFrame + 2, rate)));

        // Next-side falloff direction: the closest next frame gets the full base opacity and the
        // farther one fades. Guards against a reversed/off-by-one next-side formula (the prev tests
        // exercise a structurally different expression and cannot protect this branch).
        Assert.That(samples[0].Alpha, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(samples[1].Alpha, Is.EqualTo(0.4f * 0.5f).Within(0.0001f));
        Assert.That(samples[0].Alpha, Is.GreaterThan(samples[1].Alpha));
    }

    [Test]
    public void EnumerateOnionSkinTimes_PartialNextClamp_RenormalizesAlpha()
    {
        int rate = 30;
        TimeSpan duration = TimeSpan.FromSeconds(2); // exactly 60 frames at 30 fps
        // currentFrame = 57, nextCount=3 → frame 58 and 59 fall inside [0, duration), while
        // frame 60 lands exactly on duration (excluded by the exclusive end), so 2 next samples emit.
        int currentFrame = 57;

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: currentFrame,
            sceneStart: TimeSpan.Zero,
            sceneDuration: duration,
            frameRate: rate,
            prevCount: 0,
            nextCount: 3,
            prevBaseOpacity: 0f,
            nextBaseOpacity: 0.8f);

        Assert.That(samples, Has.Count.EqualTo(2));
        // Closest survivor lands on the full base opacity, not (3-0)/3 of a phantom 3rd sample.
        Assert.That(samples[0].Time, Is.EqualTo(FrameTime(currentFrame + 1, rate)));
        Assert.That(samples[0].Alpha, Is.EqualTo(0.8f).Within(0.0001f));
        // Farther survivor dims against the emitted count (2), not the requested count (3).
        Assert.That(samples[1].Time, Is.EqualTo(FrameTime(currentFrame + 2, rate)));
        Assert.That(samples[1].Alpha, Is.EqualTo(0.8f * 0.5f).Within(0.0001f));
    }

    [Test]
    public void EnumerateOnionSkinTimes_FractionalRate_SamplesLandOnFrameGrid()
    {
        // At 24/60 fps a frame is fractional in ticks. Sample times must align with the shared
        // frame<->time grid (frame.ToTimeSpan(rate)); stepping by a single rounded per-frame tick
        // would drift sub-frame and could even drop a valid neighbor near the scene start.
        int rate = 24;
        int currentFrame = 100;

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: currentFrame,
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: rate,
            prevCount: 3,
            nextCount: 3,
            prevBaseOpacity: 0.6f,
            nextBaseOpacity: 0.6f);

        Assert.That(samples, Has.Count.EqualTo(6));

        // prev: frames 97, 98, 99 (oldest→closest); next: frames 101, 102, 103 (closest→farthest).
        Assert.That(samples[0].Time, Is.EqualTo(FrameTime(currentFrame - 3, rate)));
        Assert.That(samples[1].Time, Is.EqualTo(FrameTime(currentFrame - 2, rate)));
        Assert.That(samples[2].Time, Is.EqualTo(FrameTime(currentFrame - 1, rate)));
        Assert.That(samples[3].Time, Is.EqualTo(FrameTime(currentFrame + 1, rate)));
        Assert.That(samples[4].Time, Is.EqualTo(FrameTime(currentFrame + 2, rate)));
        Assert.That(samples[5].Time, Is.EqualTo(FrameTime(currentFrame + 3, rate)));
    }

    [Test]
    public void EnumerateOnionSkinTimes_FirstFrameAtFractionalRate_KeepsPreviousAtSceneStart()
    {
        // Regression guard for the dropped-neighbor concern: at frame 1 on a fractional-tick rate,
        // the single previous frame is frame 0 and must land exactly on sceneStart (not slightly
        // before it, which would be clamped away).
        int rate = 24;

        var samples = OnionSkinHelper.EnumerateOnionSkinTimes(
            currentFrame: 1,
            sceneStart: TimeSpan.Zero,
            sceneDuration: TimeSpan.FromMinutes(1),
            frameRate: rate,
            prevCount: 1,
            nextCount: 0,
            prevBaseOpacity: 0.5f,
            nextBaseOpacity: 0f);

        Assert.That(samples, Has.Count.EqualTo(1));
        Assert.That(samples[0].IsPrev, Is.True);
        Assert.That(samples[0].Time, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void EditorConfig_ClampsOnionSkinCounts_ViaRangeAttribute()
    {
        // The settings tab uses a generic number editor, so the 0..10 bound the old flyout
        // enforced now lives as a [Range] on the property and is coerced on assignment.
        var config = new EditorConfig
        {
            OnionSkinPrevCount = 1_000_000,
            OnionSkinNextCount = -5,
        };

        Assert.That(config.OnionSkinPrevCount, Is.EqualTo(10));
        Assert.That(config.OnionSkinNextCount, Is.EqualTo(0));
    }

    [Test]
    public void EditorConfig_ClampsOnionSkinOpacity_ViaRangeAttribute()
    {
        var config = new EditorConfig
        {
            OnionSkinPrevOpacity = 5f,
            OnionSkinNextOpacity = -1f,
        };

        Assert.That(config.OnionSkinPrevOpacity, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(config.OnionSkinNextOpacity, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void EditorConfig_ClampsNodeCachePixels_ViaRangeAttribute()
    {
        var config = new EditorConfig
        {
            NodeCacheMaxPixels = 0,
            NodeCacheMinPixels = -10,
        };

        Assert.That(config.NodeCacheMaxPixels, Is.EqualTo(1));
        Assert.That(config.NodeCacheMinPixels, Is.EqualTo(1));
    }
}
