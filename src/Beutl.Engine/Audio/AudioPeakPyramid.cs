using System.Numerics;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Audio;

// Raw mono samples resolve partial blocks exactly. Aligned blocks select the largest available
// level, so a zoomed-out query need not scan the samples again.
internal sealed class AudioPeakPyramid
{
    private const int BlockSize = 64;
    private readonly float[] _samples;
    private readonly (float Min, float Max)[][] _levels;

    public AudioPeakPyramid(float[] samples)
    {
        _samples = samples;
        var levels = new List<(float Min, float Max)[]>();
        int count = samples.Length / BlockSize;
        if (count > 0)
        {
            var leaves = new (float Min, float Max)[count];
            for (int i = 0; i < count; i++)
            {
                var peak = (Min: float.PositiveInfinity, Max: float.NegativeInfinity);
                for (int j = i * BlockSize; j < (i + 1) * BlockSize; j++)
                    peak = Merge(peak, (samples[j], samples[j]));
                leaves[i] = peak;
            }
            levels.Add(leaves);

            while (levels[^1].Length >= 2)
            {
                var previous = levels[^1];
                var level = new (float Min, float Max)[previous.Length / 2];
                for (int i = 0; i < level.Length; i++)
                    level[i] = Merge(previous[2 * i], previous[2 * i + 1]);
                levels.Add(level);
            }
        }
        _levels = levels.ToArray();
        ByteCount = sizeof(float) * (long)samples.Length + _levels.Sum(level => 2L * sizeof(float) * level.Length);
    }

    public long ByteCount { get; }

    public (float Min, float Max) Measure(int start, int end)
    {
        var peak = (Min: float.PositiveInfinity, Max: float.NegativeInfinity);
        while (start < end)
        {
            int blocks = (end - start) / BlockSize;
            if (start % BlockSize == 0 && blocks > 0)
            {
                int first = start / BlockSize;
                int level = Math.Min(BitOperations.TrailingZeroCount((uint)first), BitOperations.Log2((uint)blocks));
                peak = Merge(peak, _levels[level][first >> level]);
                start += BlockSize << level;
            }
            else
            {
                float value = _samples[start++];
                peak = Merge(peak, (value, value));
            }
        }
        return peak;
    }

    internal static (float Min, float Max) Merge((float Min, float Max) a, (float Min, float Max) b)
        => (Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max));
}

// Each page is a one-second sequential decode with the same static routing and mastering as
// Composer. Cache the actual downmix, since independent left/right extrema miss phase cancellation.
internal sealed class AudioPeakReader : IDisposable
{
    internal readonly record struct Mix(AudioChannelMap ChannelMap, float Gain, float Pan);
    private readonly record struct SourceKey(DecoderRegistry Decoders, long DecoderVersion, ProxyFingerprint File);
    private readonly record struct PageKey(SourceKey Source, Mix Mix, long Index);

    private const long PageBudgetBytes = 64 * 1024 * 1024;
    private const int WholePageBudget = 64 * 1024;
    private static readonly object s_gate = new();
    private static readonly Dictionary<SourceKey, (int Rate, long Samples)> s_metadata = new();
    private static readonly Dictionary<PageKey, LinkedListNode<(PageKey Key, AudioPeakPyramid Page)>> s_pages = new();
    private static readonly Dictionary<PageKey, Lazy<AudioPeakPyramid?>> s_building = new();
    private static readonly Dictionary<PageKey, (float Min, float Max)> s_wholePages = new();
    private static readonly Queue<PageKey> s_wholePageOrder = new();
    private static readonly LinkedList<(PageKey Key, AudioPeakPyramid Page)> s_lru = new();
    private static long s_pageBytes;

    private readonly SourceKey _source;
    private readonly Mix _mix;
    private SoundSource.Resource? _resource;
    private AudioContext? _context;
    private GainNode? _output;
    private readonly AnimationSampler _sampler = new();

    public AudioPeakReader(DecoderRegistry decoders, ProxyFingerprint fingerprint, Mix mix)
    {
        _source = new(decoders, decoders.Version, fingerprint);
        _mix = mix;
    }

    public (int Rate, long Samples) GetMetadata()
    {
        lock (s_gate)
        {
            if (s_metadata.TryGetValue(_source, out var cached))
                return cached;
        }

        var resource = OpenResource();
        if (resource.SampleRate <= 0 || resource.Duration <= MediaTime.Zero)
            return default;
        var metadata = (resource.SampleRate, resource.Duration.ToSamples(resource.SampleRate, MidpointRounding.ToPositiveInfinity));
        lock (s_gate)
        {
            if (s_metadata.Count >= 256)
                s_metadata.Clear();
            s_metadata[_source] = metadata;
        }
        return metadata;
    }

    public (float Min, float Max)? Measure(long start, long length, int rate, long sourceSamples, CancellationToken ct)
    {
        if (start < 0 || start >= sourceSamples || start > int.MaxValue)
            return (0, 0);

        long end = start + Math.Min(length, sourceSamples - start);
        var peak = end - start == length ? (float.PositiveInfinity, float.NegativeInfinity) : (0f, 0f);
        while (start < end)
        {
            ct.ThrowIfCancellationRequested();
            long pageIndex = start / rate;
            long pageStart = pageIndex * rate;
            int pageCount = (int)Math.Min(rate, sourceSamples - pageStart);
            long windowEnd = Math.Min(end, pageStart + pageCount);
            var key = new PageKey(_source, _mix, pageIndex);
            bool wholePage = start == pageStart && windowEnd == pageStart + pageCount;
            if (wholePage && TryGetWholePage(key, out var fullPeak))
            {
                peak = AudioPeakPyramid.Merge(peak, fullPeak);
            }
            else
            {
                var page = GetPage(key, pageStart, pageCount, rate);
                if (page == null)
                    return null;
                ct.ThrowIfCancellationRequested();
                peak = AudioPeakPyramid.Merge(peak, page.Measure((int)(start - pageStart), (int)(windowEnd - pageStart)));
            }
            start = windowEnd;
        }
        return peak;
    }

    private static bool TryGetWholePage(PageKey key, out (float Min, float Max) peak)
    {
        lock (s_gate)
            return s_wholePages.TryGetValue(key, out peak);
    }

    private AudioPeakPyramid? GetPage(PageKey key, long start, int count, int rate)
    {
        Lazy<AudioPeakPyramid?> build;
        lock (s_gate)
        {
            if (s_pages.TryGetValue(key, out var cached))
            {
                s_lru.Remove(cached);
                s_lru.AddLast(cached);
                return cached.Value.Page;
            }
            if (!s_building.TryGetValue(key, out build!))
            {
                build = new(() => BuildPage(key, start, count, rate));
                s_building.Add(key, build);
            }
        }

        // The first request supplies the reader. Finish that one page even if its consumer
        // scrolls away, since another request may already be waiting for the same samples.
        try
        {
            return build.Value;
        }
        finally
        {
            lock (s_gate)
            {
                if (s_building.GetValueOrDefault(key) == build)
                    s_building.Remove(key);
            }
        }
    }

    private AudioPeakPyramid? BuildPage(PageKey key, long start, int count, int rate)
    {
        var output = OpenOutput(rate);
        if (output == null)
            return null;

        var range = new TimeRange(new MediaTime((long)(((Int128)start * MediaTime.FlicksPerSecond + rate - 1) / rate)),
            AudioProcessContext.GetDurationForSampleCount(count, rate));
        using var reads = new AudioReadScope();
        using var buffer = output.Process(new AudioProcessContext(range, rate, _sampler, range));
        if (reads.HasFailures)
            return null;
        var left = buffer.GetChannelData(0);
        var right = buffer.GetChannelData(1);
        AudioMath.ApplyLimiter(left);
        AudioMath.ApplyLimiter(right);
        AudioMath.ClampToFullScale(left);
        AudioMath.ClampToFullScale(right);
        var samples = new float[count];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (left[i] + right[i]) * 0.5f;

        var page = new AudioPeakPyramid(samples);
        lock (s_gate)
        {
            // Keep the inexpensive whole-page envelope after the detailed page is evicted. Long
            // zoomed-out ranges will then need exact samples only at their boundary pages.
            if (s_wholePages.TryAdd(key, page.Measure(0, count)))
            {
                s_wholePageOrder.Enqueue(key);
                if (s_wholePageOrder.Count > WholePageBudget)
                    s_wholePages.Remove(s_wholePageOrder.Dequeue());
            }

            if (!s_pages.ContainsKey(key) && page.ByteCount <= PageBudgetBytes)
            {
                s_pages.Add(key, s_lru.AddLast((key, page)));
                s_pageBytes += page.ByteCount;
                while (s_pageBytes > PageBudgetBytes && s_lru.First is { } oldest)
                {
                    s_lru.RemoveFirst();
                    s_pages.Remove(oldest.Value.Key);
                    s_pageBytes -= oldest.Value.Page.ByteCount;
                }
            }
        }
        return page;
    }

    private SoundSource.Resource OpenResource()
        => _resource ??= SoundSource.Open(_source.File.SourcePath).ToResource(
            new CompositionContext(MediaTime.Zero) { Decoders = _source.Decoders, IsThumbnail = true });

    private GainNode? OpenOutput(int rate)
    {
        if (_output != null)
            return _output;

        var resource = OpenResource();
        if (resource.SampleRate != rate)
            return null;

        _context = new AudioContext(rate, AudioChannelLayout.Stereo);
        var source = _context.CreateSourceNode(resource, _mix.ChannelMap);
        var panner = _context.CreatePannerNode(Property.CreateValue(_mix.Pan), Property.CreateValue(0f),
            Property.CreateValue(0f), Property.CreateValue(0f), source.ClipLayout);
        var gain = _context.CreateGainNode(Property.CreateValue(_mix.Gain));
        _context.Connect(source, panner);
        _context.Connect(panner, gain);
        return _output = gain;
    }

    public void Dispose()
    {
        _context?.Dispose();
        _resource?.Dispose();
    }
}
