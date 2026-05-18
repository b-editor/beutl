using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.UnitTests.Engine.Animation;

public class KeyFrameDeltaHelperTests
{
    private static KeyFrame<float> CreateKeyFrame(float value, double seconds = 0)
    {
        return new KeyFrame<float>
        {
            Value = value,
            KeyTime = TimeSpan.FromSeconds(seconds),
            Easing = new LinearEasing(),
        };
    }

    [Test]
    public void ApplyDelta_BothNull_ReturnsFalse()
    {
        bool result = KeyFrameDeltaHelper.ApplyDelta<float>(null, null, 0f, 0f, 10f);
        Assert.That(result, Is.False);
    }

    [Test]
    public void ApplyDelta_BothKeyFrames_ShiftsBothByDelta()
    {
        var prev = CreateKeyFrame(100f, 0);
        var next = CreateKeyFrame(200f, 1);

        bool result = KeyFrameDeltaHelper.ApplyDelta(prev, next, 100f, 200f, 25f);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(prev.Value, Is.EqualTo(125f));
            Assert.That(next.Value, Is.EqualTo(225f));
        });
    }

    [Test]
    public void ApplyDelta_OnlyPrevious_OnlyShiftsPrevious()
    {
        var prev = CreateKeyFrame(100f, 0);

        bool result = KeyFrameDeltaHelper.ApplyDelta(prev, null, 100f, 999f, 15f);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(prev.Value, Is.EqualTo(115f));
            // Confirms startNext = 999f is unused (next is null, so no write-back)
        });
    }

    [Test]
    public void ApplyDelta_OnlyNext_OnlyShiftsNext()
    {
        var next = CreateKeyFrame(200f, 1);

        bool result = KeyFrameDeltaHelper.ApplyDelta(null, next, 999f, 200f, -30f);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(next.Value, Is.EqualTo(170f));
        });
    }

    [Test]
    public void ApplyDelta_PreservesAmplitude()
    {
        // Animation-amplitude preservation invariant: (next - prev) after the shift matches the pre-shift value.
        var prev = CreateKeyFrame(100f, 0);
        var next = CreateKeyFrame(200f, 1);
        float originalAmplitude = next.Value - prev.Value;

        KeyFrameDeltaHelper.ApplyDelta(prev, next, prev.Value, next.Value, 75f);

        Assert.That(next.Value - prev.Value, Is.EqualTo(originalAmplitude));
    }

    [Test]
    public void ApplyDelta_UsesStartValuesNotCurrent_Idempotency()
    {
        // A drag triggers OnMoved repeatedly, but each call recomputes from the start snapshot,
        // so the result is the same for the same delta no matter how many times it is invoked (idempotent).
        var prev = CreateKeyFrame(100f, 0);
        var next = CreateKeyFrame(200f, 1);

        // 1st call: delta=10
        KeyFrameDeltaHelper.ApplyDelta(prev, next, 100f, 200f, 10f);
        float firstPrev = prev.Value;
        float firstNext = next.Value;

        // 2nd call with same start values and same delta should give same result.
        KeyFrameDeltaHelper.ApplyDelta(prev, next, 100f, 200f, 10f);

        Assert.Multiple(() =>
        {
            Assert.That(prev.Value, Is.EqualTo(firstPrev));
            Assert.That(next.Value, Is.EqualTo(firstNext));
            Assert.That(prev.Value, Is.EqualTo(110f));
            Assert.That(next.Value, Is.EqualTo(210f));
        });
    }

    [Test]
    public void CaptureStartValues_BothNull_ReturnsFallback()
    {
        var (prev, next) = KeyFrameDeltaHelper.CaptureStartValues<float>(null, null, 42f);

        Assert.Multiple(() =>
        {
            Assert.That(prev, Is.EqualTo(42f));
            Assert.That(next, Is.EqualTo(42f));
        });
    }

    [Test]
    public void CaptureStartValues_OnlyPrevious_NextUsesFallback()
    {
        var p = CreateKeyFrame(100f, 0);
        var (prev, next) = KeyFrameDeltaHelper.CaptureStartValues<float>(p, null, 42f);

        Assert.Multiple(() =>
        {
            Assert.That(prev, Is.EqualTo(100f));
            Assert.That(next, Is.EqualTo(42f));
        });
    }

    [Test]
    public void CaptureStartValues_BothKeyFrames_ReturnsValues()
    {
        var p = CreateKeyFrame(100f, 0);
        var n = CreateKeyFrame(200f, 1);
        var (prev, next) = KeyFrameDeltaHelper.CaptureStartValues<float>(p, n, 42f);

        Assert.Multiple(() =>
        {
            Assert.That(prev, Is.EqualTo(100f));
            Assert.That(next, Is.EqualTo(200f));
        });
    }
}
