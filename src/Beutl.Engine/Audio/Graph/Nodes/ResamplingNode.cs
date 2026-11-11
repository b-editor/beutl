using System.Buffers;
using System.Numerics;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Converts its input from <see cref="SourceSampleRate"/> to the rate it is asked for, filling the time between
/// samples with <see cref="Method"/>. The input is read ahead of the output rather than delayed behind it, so the
/// sound stays where it is on the timeline.
/// </summary>
/// <remarks>
/// <see cref="ResampleNode"/> converts with a fixed method, and passes through what this node has converted already.
/// </remarks>
public sealed class ResamplingNode : AudioNode, IAudioSampleRateConversionNode
{
    private const int MaxReadFrames = 16384;

    // How many output samples are rendered from one read of the input.
    internal const int PortionFrames = 8192;

    private int _sampleRate;
    private AudioSourceStream? _stream;
    private SlidingWindow<float>? _input;
    private MediaTime? _lastTimeRangeEnd;
    private float[] _weights = [];

    private ResamplingKernel _kernel = ResamplingKernel.For(ResamplingMethod.Hold);
    private long _outputPosition;

    // A sinc's cutoff as a fraction of the input's Nyquist frequency, and how far before an output's position in the
    // input the samples it is read from can begin.
    private double _cutoff;
    private long _reach;

    // Live input stops at _inputEnd. The upstream's held tail follows until _drainEnd (null when the upstream reports
    // an unbounded tail), and silence after that.
    private long? _inputEnd;
    private long? _liveEnd;
    private long? _drainEnd;
    private bool _draining;

    // The input format is supplied when the graph is built, without pulling upstream audio.
    public int ChannelCount { get; init; } = 2;

    /// <summary>The rate the input is read at, in Hz.</summary>
    public required int SourceSampleRate
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    }

    public required IProperty<ResamplingMethod> Method { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: false));

    public override AudioBuffer Flush(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: true));

    // The input's latency is counted at its own rate, rounded up so that the budget at the output never falls short.
    public override int GetTotalLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return AudioLatency.ScaleSampleCount(base.GetTotalLatencySamples(SourceSampleRate), SourceSampleRate, sampleRate);
    }

    public override int GetDrainLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return AudioLatency.ScaleSampleCount(base.GetDrainLatencySamples(SourceSampleRate), SourceSampleRate, sampleRate);
    }

    private AudioBuffer ProcessCore(AudioProcessContext context, bool draining)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Resampling node requires exactly one input.");

        if (context.SampleRate == SourceSampleRate)
        {
            // Whatever follows starts over from the position it is asked for.
            _lastTimeRangeEnd = null;
            return draining ? Inputs[0].Flush(context) : Inputs[0].Process(context);
        }

        int count = context.GetSampleCount();
        var output = new AudioBuffer(context.SampleRate, ChannelCount, count);
        // An empty chunk carries no audio, and advancing the end here would mask a discontinuity.
        if (count == 0)
            return output;

        try
        {
            if (Prepare(context, draining))
                Render(output, context);

            return output;
        }
        catch
        {
            // A chunk that threw must not look contiguous, or the next one inherits half-read input.
            _lastTimeRangeEnd = null;
            output.Dispose();
            throw;
        }
    }

    // Returns false when a drain has nothing to release.
    private bool Prepare(AudioProcessContext context, bool draining)
    {
        int sampleRate = context.SampleRate;
        if (_input == null || _sampleRate != sampleRate)
        {
            _sampleRate = sampleRate;
            _stream = new AudioSourceStream(SourceSampleRate, ChannelCount, static time => time);
            _input = new SlidingWindow<float>(ChannelCount);
            _lastTimeRangeEnd = null;
        }

        bool contiguous = _lastTimeRangeEnd is { } end && context.ContinuesFrom(end);
        if (draining)
        {
            // A drain continues where the last block ended; after a seek nothing is held.
            if (!contiguous)
                return false;

            if (!_draining)
                BeginDrain();

            _lastTimeRangeEnd = context.TimeRange.End;
            return true;
        }

        // A method has nothing to interpolate, so it holds for the block.
        ResamplingMethod method = context.AnimationSampler.SampleAt(Method, context.TimeRange.Start, sampleRate);
        long? inputEnd = _stream!.GetInputEnd(Inputs[0], context);

        // An edit can move the end without replacing the node: input read past a nearer end must go, and input that
        // stopped at an earlier end has to go on.
        bool endMoved = _liveEnd is { } liveEnd
            ? inputEnd != liveEnd
            : inputEnd is { } value && value < _input.End;
        if (!contiguous || _draining || endMoved)
        {
            // Truncate like SourceNode and ResampleNode, so a start between samples maps to the sample the upstream
            // delivers there.
            Restart(context.TimeRange.Start.ToSamples(sampleRate, MidpointRounding.ToZero), inputEnd);
        }
        else
        {
            _inputEnd = inputEnd;
        }

        // The input keeps what the widest kernel reaches back to, so a new method takes over without reading it
        // again, which an echo or a limiter above would take for a seek.
        _kernel = ResamplingKernel.For(method);
        _lastTimeRangeEnd = context.TimeRange.End;
        return true;
    }

    // Rereads the input from far enough before the output for every sample the output depends on.
    private void Restart(long outputStart, long? inputEnd)
    {
        _inputEnd = inputEnd;
        _liveEnd = null;
        _drainEnd = null;
        _draining = false;
        _outputPosition = outputStart;

        // Converting to a lower rate, a sinc narrows to the output's Nyquist frequency by stretching. The input is kept
        // as far back as Sinc 64, the widest kernel, reaches whatever the method, for a method that changes mid-stream.
        _cutoff = Math.Min(1, _sampleRate / (double)SourceSampleRate);
        _reach = (long)Math.Ceiling(ResamplingKernel.MaxRadius / _cutoff);

        // Nothing is read before the timeline origin, as playback from the start has nothing behind it.
        long readStart = Math.Max((long)Math.Floor(PositionInInput(outputStart)) - _reach - 1, 0);
        _input!.Reset(readStart);
        double seconds = readStart / (double)SourceSampleRate;
        _stream!.Begin(seconds, seconds, forceReanchor: true);
    }

    // Where an output sample falls in the input, in input samples. From the position alone, so that a seek lands on
    // the samples playback does.
    private double PositionInInput(long outputPosition)
        => outputPosition * (double)SourceSampleRate / _sampleRate;

    private void BeginDrain()
    {
        _draining = true;
        long boundary = (long)Math.Ceiling(PositionInInput(_outputPosition));
        if (_liveEnd is { } liveUntil && liveUntil <= boundary)
            return;

        // The live input has not ended by the boundary: reading stopped right at it, or ran on past it when the end
        // was not known. The upstream cannot be rewound without losing its state, such as a delay line's echoes, so
        // live input read over the tail's span stands in for the tail and anything read beyond it becomes silence.
        SlidingWindow<float> input = _input!;
        int latency = GetMaxInputLatency(SourceSampleRate, drain: true);
        long tailEnd = latency == int.MaxValue ? long.MaxValue : boundary + latency;
        if (tailEnd < input.End)
        {
            input.TruncateAt(tailEnd);
            _liveEnd = boundary;
            _drainEnd = tailEnd;
        }
        else if (_liveEnd is null)
        {
            // Everything read so far lies within the tail; the upstream drains the rest.
            _liveEnd = input.End;
            _drainEnd = latency == int.MaxValue ? null : tailEnd;
        }
    }

    private void Render(AudioBuffer output, AudioProcessContext context)
    {
        ResamplingKernel kernel = _kernel;
        SlidingWindow<float> input = _input!;
        bool stretched = kernel.IsSinc && _cutoff < 1;
        int count = output.SampleCount;
        Span<float> raw = output.GetRawSpan();
        long position = _outputPosition;
        for (int i = 0; i < count; i++, position++)
        {
            // A portion's input at once: read as each output needs it, it would reach every node above, and a decoder,
            // thousands of times a second, and read for a whole long block, such as a waveform's, it would all be held.
            if (i % PortionFrames == 0)
            {
                double last = PositionInInput(position + Math.Min(PortionFrames, count - i) - 1);
                long end = stretched
                    ? (long)Math.Ceiling(last + kernel.Radius / _cutoff)
                    : (long)Math.Floor(last) + kernel.Last + 1;
                EnsureInput(end, context);
            }

            double at = PositionInInput(position);
            long whole = (long)Math.Floor(at);
            long first;
            int taps;
            if (stretched)
            {
                double reach = kernel.Radius / _cutoff;
                first = (long)Math.Floor(at - reach) + 1;
                taps = checked((int)((long)Math.Ceiling(at + reach) - first));
                kernel.GetSincWeights(at, first, _cutoff, RentWeights(taps));
            }
            else
            {
                first = whole + kernel.First;
                taps = kernel.Taps;
                kernel.GetWeights(at - whole, RentWeights(taps));
            }

            EnsureInput(first + taps, context);

            // Input before the first read is silence.
            long from = Math.Max(first, input.Start);
            int skip = (int)Math.Min(from - first, taps);
            if (skip < taps)
            {
                int index = input.IndexOf(from);
                ReadOnlySpan<float> used = _weights.AsSpan(skip, taps - skip);
                for (int ch = 0; ch < ChannelCount; ch++)
                    raw[ch * count + i] = Dot(input[ch].AsSpan(index, taps - skip), used);
            }

            // Later outputs sit further on and reach back no further than _reach, so the input before that is done.
            input.DropBefore(whole - _reach - 1);
        }

        _outputPosition = position;
    }

    private Span<float> RentWeights(int taps)
    {
        if (_weights.Length < taps)
            _weights = new float[Math.Max(taps, _weights.Length * 2)];

        return _weights.AsSpan(0, taps);
    }

    // Live input up to the end, then the upstream's held tail, then silence.
    private void EnsureInput(long end, AudioProcessContext context)
    {
        SlidingWindow<float> input = _input!;
        while (input.End < end)
        {
            long position = input.End;
            int frames = (int)Math.Min(end - position, MaxReadFrames);
            if (_liveEnd is null)
            {
                if ((_inputEnd is not { } inputEnd || position < inputEnd)
                    && Read(frames, draining: false, _inputEnd, context) > 0)
                {
                    continue;
                }

                EndLiveInput(position);
            }

            if (_drainEnd is not { } drainEnd || position < drainEnd)
            {
                if (Read(frames, draining: true, _drainEnd, context) > 0)
                    continue;

                _drainEnd = position;
            }

            int index = input.Extend(frames);
            for (int ch = 0; ch < ChannelCount; ch++)
                Array.Clear(input[ch], index, frames);
        }
    }

    private void EndLiveInput(long position)
    {
        _liveEnd = position;
        int latency = GetMaxInputLatency(SourceSampleRate, drain: true);
        _drainEnd = latency == int.MaxValue ? null : position + latency;
    }

    private int Read(int frames, bool draining, long? end, AudioProcessContext context)
    {
        int channels = ChannelCount;
        int length = frames * channels;
        float[] buffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            int read = _stream!.Read(buffer.AsSpan(0, length), Inputs[0], context, draining, end) / channels;
            if (read > 0)
            {
                SlidingWindow<float> input = _input!;
                int index = input.Extend(read);
                for (int ch = 0; ch < channels; ch++)
                {
                    float[] data = input[ch];
                    for (int i = 0; i < read; i++)
                        data[index + i] = buffer[i * channels + ch];
                }
            }

            return read;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    private static float Dot(ReadOnlySpan<float> values, ReadOnlySpan<float> weights)
    {
        float sum = 0;
        int i = 0;
        if (Vector.IsHardwareAccelerated && values.Length >= Vector<float>.Count)
        {
            var accumulator = Vector<float>.Zero;
            for (; i <= values.Length - Vector<float>.Count; i += Vector<float>.Count)
                accumulator += new Vector<float>(values[i..]) * new Vector<float>(weights[i..]);

            sum = Vector.Sum(accumulator);
        }

        for (; i < values.Length; i++)
            sum += values[i] * weights[i];

        return sum;
    }

    protected override void Dispose(bool disposing)
    {
        _stream = null;
        _input = null;
        base.Dispose(disposing);
    }
}
