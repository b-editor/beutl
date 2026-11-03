using System.Buffers;
using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Engine;

namespace Beutl.Audio.Graph.Nodes;

// Stretches the input with WSOLA, which joins its grains where their waveforms agree, then plays the result back at
// the rate the pitch asks for, so the two cancel out in time. The input is read ahead of the output rather than
// delayed behind it, so the sound stays where it is on the timeline.
public sealed class PitchShiftNode : AudioNode
{
    private const int FeedFrames = 1024;

    // The stretcher holds its tempo for this many frames, so a keyframed pitch moves in steps this small.
    private const int StretchedBlockFrames = 64;

    private WsolaTimeStretcher? _stretcher;
    private AudioSourceStream? _stream;
    private List<AudioNode>? _upstreamSnapshot;
    private int _sampleRate;
    private int _channels;
    private MediaTime? _lastTimeRangeEnd;
    private float _lastSemitones;
    private long? _inputEnd;
    private bool _flushed;
    private bool _draining;

    // The stretched frames not played yet, the two being played, and how far playback is between them.
    private float[] _stretched = [];
    private float[] _feed = [];
    private float[] _previous = [];
    private float[] _next = [];
    private int _stretchedPosition;
    private int _stretchedCount;
    private double _between;
    private bool _started;

    // The input format is supplied when the graph is built, without pulling upstream audio.
    public int ChannelCount { get; set; } = 2;

    public required IProperty<float> Semitones { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: false));

    public override AudioBuffer Flush(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: true));

    private bool IsUnshifted =>
        Semitones.Value == 0
        && Semitones.GetKeyFrameAnimation() == null
        && Semitones is not IExpressionProperty { HasExpression: true };

    private AudioBuffer ProcessCore(AudioProcessContext context, bool draining)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Pitch shift node requires exactly one input.");

        if (IsUnshifted)
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

        float[] interleaved = ArrayPool<float>.Shared.Rent(Math.Min(count, 1024) * ChannelCount);
        try
        {
            if (Prepare(context, draining))
                Play(output, interleaved, context, draining);

            return output;
        }
        catch
        {
            // A chunk that threw must not look contiguous, or the next one inherits half-read input.
            _lastTimeRangeEnd = null;
            output.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
        }
    }

    // Returns false when a drain has nothing to release.
    private bool Prepare(AudioProcessContext context, bool draining)
    {
        bool upstreamChanged = AudioSourceStream.UpstreamChangedAndCapture(this, ref _upstreamSnapshot);
        if (_stretcher == null || _sampleRate != context.SampleRate || _channels != ChannelCount || upstreamChanged)
        {
            _sampleRate = context.SampleRate;
            _channels = ChannelCount;
            _stretcher = new WsolaTimeStretcher(_sampleRate, _channels);
            _stream = new AudioSourceStream(_sampleRate, _channels, static time => time);
            _stretched = new float[StretchedBlockFrames * _channels];
            _feed = new float[FeedFrames * _channels];
            _previous = new float[_channels];
            _next = new float[_channels];
            _lastTimeRangeEnd = null;
        }

        bool contiguous = _lastTimeRangeEnd is { } end && context.ContinuesFrom(end);
        if (draining)
        {
            // A drain continues where the last block ended; after a seek nothing is held.
            if (!contiguous)
                return false;

            if (!_draining)
            {
                // The input stops at the terminal, so the upstream's held tail starts right there, and what
                // the stretcher read ahead for the live audio has no part in it.
                Reset();
                _draining = true;
                int latency = GetMaxInputLatency(_sampleRate, drain: true);
                _inputEnd = latency == int.MaxValue ? null : checked(_stream!.Position + latency);
            }

            _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;
            return true;
        }

        long? inputEnd = GetInputEnd(context);
        // An edit can move the end without replacing this node: input read past a nearer end must go, and a
        // stretcher that has already released its last window cannot take more.
        bool endMoved = inputEnd is { } value
            ? _inputEnd is { } previous ? value < previous : _stream!.Position > value
            : false;
        bool reopened = _flushed && _inputEnd is { } old && (inputEnd is null || inputEnd > old);
        if (!contiguous || _draining || endMoved || reopened)
        {
            Reset();
            double start = context.TimeRange.Start.ToSeconds();
            _stream!.Begin(start, start, forceReanchor: true);
        }

        _inputEnd = inputEnd;
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;
        return true;
    }

    private long? GetInputEnd(AudioProcessContext context)
    {
        double? end = Inputs[0].GetFiniteSourceEndSample(_sampleRate);
        if (context.ProcessEndTime is { } terminal)
        {
            double terminalSample = terminal.ToSeconds() * _sampleRate;
            end = end is { } finite ? Math.Min(finite, terminalSample) : terminalSample;
        }

        if (end is not { } value || !double.IsFinite(value))
            return null;

        double rounded = Math.Round(value);
        return checked((long)(Math.Abs(value - rounded) < 1e-6 ? rounded : Math.Ceiling(value)));
    }

    private void Reset()
    {
        _stretcher!.Clear();
        _flushed = false;
        _draining = false;
        _inputEnd = null;
        _stretchedPosition = 0;
        _stretchedCount = 0;
        _between = 0;
        _started = false;
    }

    private void Play(AudioBuffer output, float[] interleaved, AudioProcessContext context, bool draining)
    {
        int count = output.SampleCount;
        int chunkLimit = Math.Min(count, 1024);
        float[] rented = ArrayPool<float>.Shared.Rent(chunkLimit);
        try
        {
            Span<float> semitones = rented.AsSpan(0, chunkLimit);
            int processed = 0;
            while (processed < count)
            {
                int chunk = Math.Min(chunkLimit, count - processed);
                if (draining)
                {
                    semitones[..chunk].Fill(_lastSemitones);
                }
                else
                {
                    context.AnimationSampler.SampleBuffer(
                        Semitones, context.GetChunkRange(processed, chunk), context.SampleRate, semitones[..chunk]);
                    _lastSemitones = semitones[chunk - 1];
                }

                PlayChunk(semitones[..chunk], interleaved, context, draining);
                for (int ch = 0; ch < _channels; ch++)
                {
                    Span<float> destination = output.GetChannelData(ch).Slice(processed, chunk);
                    for (int i = 0; i < chunk; i++)
                        destination[i] = interleaved[i * _channels + ch];
                }

                processed += chunk;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }

        _stream!.Complete(context, count);
    }

    private void PlayChunk(ReadOnlySpan<float> semitones, Span<float> interleaved, AudioProcessContext context, bool draining)
    {
        for (int i = 0; i < semitones.Length; i++)
        {
            // An expression or a hand-edited file can give any value, and playback reads this many frames per sample.
            float shift = float.IsNaN(semitones[i]) ? 0 : Math.Clamp(semitones[i], PitchShiftEffect.SemitonesMin, PitchShiftEffect.SemitonesMax);
            double rate = Math.Pow(2, shift / 12);
            // Stretched by the inverse of the playback rate, the source passes one sample for each one played.
            _stretcher!.Tempo = 1 / rate;
            if (!_started)
            {
                ReadFrame(_previous, context, draining);
                ReadFrame(_next, context, draining);
                _started = true;
            }

            for (int ch = 0; ch < _channels; ch++)
                interleaved[i * _channels + ch] = _previous[ch] + (_next[ch] - _previous[ch]) * (float)_between;

            _between += rate;
            while (_between >= 1)
            {
                _between -= 1;
                (_previous, _next) = (_next, _previous);
                ReadFrame(_next, context, draining);
            }
        }
    }

    // Past the end of the input the frame is silence.
    private void ReadFrame(float[] frame, AudioProcessContext context, bool draining)
    {
        if (_stretchedPosition == _stretchedCount && !Refill(context, draining))
        {
            Array.Clear(frame);
            return;
        }

        _stretched.AsSpan(_stretchedPosition * _channels, _channels).CopyTo(frame);
        _stretchedPosition++;
    }

    private bool Refill(AudioProcessContext context, bool draining)
    {
        _stretchedPosition = 0;
        _stretchedCount = 0;
        while (true)
        {
            _stretchedCount = _stretcher!.ReceiveSamples(_stretched, StretchedBlockFrames);
            if (_stretchedCount > 0)
                return true;
            if (_flushed)
                return false;

            int got = _stream!.Read(_feed, Inputs[0], context, draining, _inputEnd) / _channels;
            if (got > 0)
                _stretcher.PutSamples(_feed.AsSpan(0, got * _channels), got);
            // A short or exhausted source must release its final processing window once.
            if (got == 0 || (_inputEnd is { } end && _stream.Position >= end))
            {
                _stretcher.Flush();
                _flushed = true;
            }
        }
    }
}
