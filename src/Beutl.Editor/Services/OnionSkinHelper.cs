using Beutl.Media;

namespace Beutl.Editor.Services;

public readonly record struct OnionSkinSample(MediaTime Time, bool IsPrev, float Alpha);

public static class OnionSkinHelper
{
    // The previous/next frame times to draw as onion skins: the closest at the base opacity, farther ones fading
    // linearly. Samples outside the scene are omitted, and alpha is spread over the emitted samples only, so a sample
    // cut off near the scene start does not dim the near frames.
    //
    // Sample times are derived from absolute frame numbers with MediaTime.FromFrames, so they land on
    // exactly the frame grid the player uses.
    public static IReadOnlyList<OnionSkinSample> EnumerateOnionSkinTimes(
        int currentFrame, MediaTime sceneStart, MediaTime sceneDuration,
        Rational frameRate, int prevCount, int nextCount,
        float prevBaseOpacity, float nextBaseOpacity)
    {
        if (frameRate.Numerator <= 0 || frameRate.Denominator <= 0)
            return [];
        prevCount = Math.Max(0, prevCount);
        nextCount = Math.Max(0, nextCount);
        if (prevCount == 0 && nextCount == 0)
            return [];

        MediaTime minTime = sceneStart;
        MediaTime maxTime = sceneStart + sceneDuration; // exclusive

        // Collect prev times in oldest→closest order. Alpha is filled in after the actual
        // emitted count is known so falloff stays continuous when the loop skipped frames
        // that fell outside the scene range.
        var prevTimes = new List<MediaTime>(prevCount);
        for (int i = prevCount; i >= 1; i--)
        {
            var t = MediaTime.FromFrames(currentFrame - i, frameRate);
            if (t < minTime || t >= maxTime)
                continue;
            prevTimes.Add(t);
        }

        var nextTimes = new List<MediaTime>(nextCount);
        for (int i = 1; i <= nextCount; i++)
        {
            var t = MediaTime.FromFrames(currentFrame + i, frameRate);
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
        MediaTime currentTime,
        bool onionSkinEnabled,
        int prevCount, float prevOpacity,
        int nextCount, float nextOpacity,
        Rational frameRate, MediaTime sceneStart, MediaTime sceneDuration)
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

        if (frameRate.Numerator <= 0 || frameRate.Denominator <= 0)
            return false;

        int frame = (int)currentTime.ToFrame(frameRate, MidpointRounding.AwayFromZero);
        IReadOnlyList<OnionSkinSample> samples = EnumerateOnionSkinTimes(
            frame, sceneStart, sceneDuration, frameRate,
            effectivePrev, effectiveNext, prevOpacity, nextOpacity);

        return samples.Any(s => affectedRange.Any(v => v.Contains(s.Time)));
    }
}
