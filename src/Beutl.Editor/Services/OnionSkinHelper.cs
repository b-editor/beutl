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
    public static IReadOnlyList<OnionSkinSample> EnumerateOnionSkinTimes(
        TimeSpan current, TimeSpan sceneStart, TimeSpan sceneDuration,
        int frameRate, int prevCount, int nextCount,
        float prevBaseOpacity, float nextBaseOpacity)
    {
        if (frameRate <= 0)
            return [];
        prevCount = Math.Max(0, prevCount);
        nextCount = Math.Max(0, nextCount);
        if (prevCount == 0 && nextCount == 0)
            return [];

        long tickPerFrame = TimeSpan.FromSeconds(1d / frameRate).Ticks;
        TimeSpan minTime = sceneStart;
        TimeSpan maxTime = sceneStart + sceneDuration; // exclusive

        // Collect prev times in oldest→closest order. Alpha is filled in after the actual
        // emitted count is known so falloff stays continuous when the loop skipped frames
        // that fell outside the scene range.
        var prevTimes = new List<TimeSpan>(prevCount);
        for (int i = prevCount; i >= 1; i--)
        {
            TimeSpan t = current - TimeSpan.FromTicks(tickPerFrame * i);
            if (t < minTime || t >= maxTime)
                continue;
            prevTimes.Add(t);
        }

        var nextTimes = new List<TimeSpan>(nextCount);
        for (int i = 1; i <= nextCount; i++)
        {
            TimeSpan t = current + TimeSpan.FromTicks(tickPerFrame * i);
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
}
