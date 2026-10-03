using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.Editor.Services;

// Approximate the discrete playback sum with a bounded piecewise-linear table.
// Work depends on curve complexity, not the number of audio samples before the
// clip. The error allowance travels with the value into source-window checks.
internal sealed class EditorSpeedIntegral(IAnimation<float> animation, int sampleRate)
{
    internal readonly record struct Estimate(double Seconds, double Error);

    private sealed record Segment(long Start, long Count, double First, double Step,
        double ErrorPerSample, double PrefixSum, double PrefixError)
    {
        public double Sum(long count) => count * First + Step * count * (count - 1d) / 2;
    }

    private readonly List<Segment> _segments = [];
    private readonly Dictionary<long, float> _samples = [];
    private double _total;
    private double _error;
    private long _first;
    private long _end;
    private readonly bool _customInterpolation = animation.GetType() != typeof(KeyFrameAnimation<float>)
        || animation is KeyFrameAnimation<float> keys
            && keys.KeyFrames.Any(k => k.Easing.GetType().Assembly != typeof(Easing).Assembly);

    public bool HasCustomInterpolation => _customInterpolation;

    public Estimate Integrate(TimeSpan time)
        => Integrate(TimeSpan.Zero, time);

    public Estimate Integrate(TimeSpan start, TimeSpan end)
    {
        if (start == end) return default;
        double startSamples = start.TotalSeconds * sampleRate;
        double endSamples = end.TotalSeconds * sampleRate;
        long startWhole = (long)startSamples;
        long endWhole = (long)endSamples;
        EnsureTable(startWhole);
        EnsureTable(endWhole);
        (double startSum, double startError) = Prefix(startWhole);
        (double endSum, double endError) = Prefix(endWhole);
        double seconds = (endSum - startSum) / (100d * sampleRate);
        seconds += animation.Interpolate(end) * (endSamples - endWhole) / (100d * sampleRate);
        seconds -= animation.Interpolate(start) * (startSamples - startWhole) / (100d * sampleRate);
        // Account for the rounding accumulated by playback's repeated additions,
        // as well as the table's interpolation allowance.
        double magnitude = Math.Max(Math.Abs(startSum), Math.Abs(endSum)) / (100d * sampleRate);
        double rounding = magnitude * (Math.Abs(endSamples - startSamples) + 2d * sampleRate) * 4.440892098500626e-16;
        return new Estimate(seconds, Math.Abs(endError - startError) / (100d * sampleRate) + rounding);
    }

    private void EnsureTable(long target)
    {
        if (_segments.Count > 0 && target >= _first && target <= _end) return;
        var boundaries = new SortedSet<long> { Math.Min(0, target), Math.Max(1, target) };
        if (_segments.Count > 0)
        {
            boundaries.Add(_first);
            boundaries.Add(_end);
        }
        if (animation is KeyFrameAnimation<float> keys)
        {
            foreach (IKeyFrame key in keys.KeyFrames)
                boundaries.Add((long)Math.Ceiling(key.KeyTime.TotalSeconds * sampleRate));
        }
        else
        {
            boundaries.Add((long)Math.Ceiling(animation.Duration.TotalSeconds * sampleRate));
        }
        boundaries.Add(0);
        _segments.Clear();
        _samples.Clear();
        _total = 0;
        _error = 0;
        long[] points = boundaries.ToArray();
        _first = points[0];
        _end = points[^1];
        for (int i = 1; i < points.Length; i++)
            Build(points[i - 1], points[i], 0);
    }

    private float Sample(long index)
    {
        if (!_samples.TryGetValue(index, out float value))
        {
            value = animation.Interpolate(TimeSpan.FromSeconds(index / (double)sampleRate));
            _samples[index] = value;
        }
        return value;
    }

    private void Build(long first, long end, int depth)
    {
        long count = end - first;
        if (count <= 16)
        {
            for (long i = first; i < end; i++) Add(i, 1, Sample(i), 0, 0);
            return;
        }
        double a = Sample(first);
        double b = Sample(end - 1);
        double step = (b - a) / (count - 1d);
        double deviation = 0;
        double magnitude = Math.Max(Math.Abs(a), Math.Abs(b));
        // Include a nonuniform probe to avoid aliasing regularly spaced easing peaks.
        foreach (double fraction in new[] { 0.25, 0.3819660112501051, 0.5, 0.75 })
        {
            long offset = (long)((count - 1) * fraction);
            double actual = Sample(first + offset);
            deviation = Math.Max(deviation, Math.Abs(actual - (a + step * offset)));
            magnitude = Math.Max(magnitude, Math.Abs(actual));
        }
        double floatRounding = Math.ScaleB(magnitude, -21);
        if (depth < 10 && deviation > Math.Max(1e-4, floatRounding))
        {
            long middle = first + count / 2;
            Build(first, middle, depth + 1);
            Build(middle, end, depth + 1);
            return;
        }
        double allowance = deviation * 4 + floatRounding;
        if (_customInterpolation)
        {
            // Black-box easings can hide arbitrarily narrow peaks between probes.
            // Only their declared range, never sampled smoothness, bounds the error.
            allowance = animation.TryGetOutputRange(out float minimum, out float maximum)
                ? Math.Max(Math.Max(Math.Abs(a - minimum), Math.Abs(b - minimum)),
                    Math.Max(Math.Abs(a - maximum), Math.Abs(b - maximum))) + floatRounding
                : double.PositiveInfinity;
        }
        Add(first, count, a, step, allowance);
    }

    private void Add(long first, long count, double value, double step, double error)
    {
        var segment = new Segment(first, count, value, step, error, _total, _error);
        _segments.Add(segment);
        _total += segment.Sum(count);
        _error += count * error;
    }

    private (double Sum, double Error) Prefix(long index)
    {
        if (index == _end) return (_total, _error);
        int low = 0;
        int high = _segments.Count - 1;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (_segments[middle].Start <= index) low = middle;
            else high = middle - 1;
        }
        Segment segment = _segments[low];
        long count = index - segment.Start;
        return (segment.PrefixSum + segment.Sum(count), segment.PrefixError + count * segment.ErrorPerSample);
    }
}
