using Beutl.Media;

namespace Beutl.Editor.Services;

public readonly record struct OnionSkinSample(TimeSpan Time, bool IsPrev, float Alpha);

public static class OnionSkinHelper
{
    // Enumerate the previous/next frame times to render as semi-transparent onion-skin overlays.
    // Closest frames receive `baseOpacity`; farther frames fade out linearly. Samples outside
    // [sceneStart, sceneStart + sceneDuration) are omitted so callers do not need to clamp.
    // Alpha is re-normalized against the number of samples actually emitted so a partial
    // clamp (e.g. prevCount=3 near the scene start) does not leave a near frame at a lower
    // opacity than baseOpacity.
    //
    // Sample times are derived from absolute frame numbers via the shared frame<->time
    // conversion (int.ToTimeSpan(rate)), so they land on exactly the same frame grid the
    // player uses. Stepping by a single rounded per-frame tick would instead accumulate a
    // sub-frame drift at rates whose frame duration is fractional in ticks (e.g. 24/60 fps).
    public static IReadOnlyList<OnionSkinSample> EnumerateOnionSkinTimes(
        int currentFrame, TimeSpan sceneStart, TimeSpan sceneDuration,
        int frameRate, int prevCount, int nextCount,
        float prevBaseOpacity, float nextBaseOpacity)
    {
        if (frameRate <= 0)
            return [];
        prevCount = Math.Max(0, prevCount);
        nextCount = Math.Max(0, nextCount);
        if (prevCount == 0 && nextCount == 0)
            return [];

        TimeSpan minTime = sceneStart;
        TimeSpan maxTime = sceneStart + sceneDuration; // exclusive

        // Collect prev times in oldest→closest order. Alpha is filled in after the actual
        // emitted count is known so falloff stays continuous when the loop skipped frames
        // that fell outside the scene range.
        var prevTimes = new List<TimeSpan>(prevCount);
        for (int i = prevCount; i >= 1; i--)
        {
            TimeSpan t = (currentFrame - i).ToTimeSpan(frameRate);
            if (t < minTime || t >= maxTime)
                continue;
            prevTimes.Add(t);
        }

        var nextTimes = new List<TimeSpan>(nextCount);
        for (int i = 1; i <= nextCount; i++)
        {
            TimeSpan t = (currentFrame + i).ToTimeSpan(frameRate);
            if (t < minTime || t >= maxTime)
                continue;
            nextTimes.Add(t);
        }

        var samples = new List<OnionSkinSample>(prevTimes.Count + nextTimes.Count);

        int prevEmitted = prevTimes.Count;
        for (int j = 0; j < prevEmitted; j++)
        {
            // j=0 (oldest) → 1/N, j=prevEmitted-1 (closest) → 1.0
            float falloff = prevEmitted == 1 ? 1f : (float)(j + 1) / prevEmitted;
            float alpha = Math.Clamp(prevBaseOpacity * falloff, 0f, 1f);
            samples.Add(new OnionSkinSample(prevTimes[j], true, alpha));
        }

        int nextEmitted = nextTimes.Count;
        for (int j = 0; j < nextEmitted; j++)
        {
            // j=0 (closest) → 1.0, j=nextEmitted-1 (farthest) → 1/N
            float falloff = nextEmitted == 1 ? 1f : (float)(nextEmitted - j) / nextEmitted;
            float alpha = Math.Clamp(nextBaseOpacity * falloff, 0f, 1f);
            samples.Add(new OnionSkinSample(nextTimes[j], false, alpha));
        }

        return samples;
    }

    // Decide whether an edit touching `affectedRange` changes any frame the preview is
    // currently showing: the playhead frame, plus the onion-skin neighbor frames while the
    // overlay is active. Onion-skin settings are passed in (snapshotted by the caller on the
    // UI thread) so this stays a pure function and does not read shared mutable config off-thread.
    public static bool IsEditAffectingPreview(
        IReadOnlyList<TimeRange> affectedRange,
        TimeSpan currentTime,
        bool onionSkinEnabled,
        int prevCount, float prevOpacity,
        int nextCount, float nextOpacity,
        int frameRate, TimeSpan sceneStart, TimeSpan sceneDuration)
    {
        if (affectedRange.Any(v => v.Contains(currentTime)))
            return true;

        if (!onionSkinEnabled)
            return false;

        // Mirror the opacity-folding the render path uses: a zero-opacity side contributes
        // nothing, so it should not keep the preview alive either.
        int effectivePrev = prevOpacity > 0f ? prevCount : 0;
        int effectiveNext = nextOpacity > 0f ? nextCount : 0;
        if (effectivePrev == 0 && effectiveNext == 0)
            return false;

        int frame = (int)Math.Round(currentTime.ToFrameNumber(frameRate), MidpointRounding.AwayFromZero);
        IReadOnlyList<OnionSkinSample> samples = EnumerateOnionSkinTimes(
            frame, sceneStart, sceneDuration, frameRate,
            effectivePrev, effectiveNext, prevOpacity, nextOpacity);

        return samples.Any(s => affectedRange.Any(v => v.Contains(s.Time)));
    }
}
