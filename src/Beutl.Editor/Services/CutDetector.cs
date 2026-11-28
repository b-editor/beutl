using Beutl.Media;

namespace Beutl.Editor.Services;

// One frame sample: its scene time and how different it looks from the previous sample.
internal readonly record struct CutFrameScore(MediaTime Time, double Score);

internal static class CutDetector
{
    internal static IReadOnlyList<MediaTime> Detect(IReadOnlyList<CutFrameScore> frames, TimeRange clipRange,
        Rational frameRate, double threshold, MediaTime minimumInterval)
    {
        if (frames.Count == 0 || !double.IsFinite(threshold) || minimumInterval <= MediaTime.Zero)
            return [];

        // Strongest first: a weak neighbour inside the minimum interval is the same visual change,
        // not a second cut. This also lets a low threshold produce one cut per shot change.
        var accepted = new List<CutFrameScore>();
        foreach (CutFrameScore frame in frames.Where(frame => frame.Score >= threshold)
                     .OrderByDescending(frame => frame.Score))
        {
            if (accepted.Any(cut => TooClose(cut, frame))) continue;
            accepted.Add(frame);
        }

        bool TooClose(CutFrameScore left, CutFrameScore right)
        {
            MediaTime delta = left.Time - right.Time;
            return Math.Abs((decimal)delta.Flicks) < minimumInterval.Flicks;
        }

        // A split needs a frame on each side. Rounding inward would move the cut off the frame
        // the decoder reported, so candidates that cannot be split are dropped instead.
        MediaTime frameDuration = MediaTime.FromFrames(1, frameRate);
        MediaTime first = clipRange.Start + frameDuration;
        MediaTime last = clipRange.End - frameDuration;
        var result = new List<MediaTime>(accepted.Count);
        foreach (CutFrameScore frame in accepted)
        {
            MediaTime time = frame.Time.RoundToFrame(frameRate, MidpointRounding.AwayFromZero);
            if (time < first || time > last || result.Contains(time)) continue;
            result.Add(time);
        }

        result.Sort();
        return result;
    }
}
