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

    private WsolaFeed? _feed;
    private int _sampleRate;
    private MediaTime? _lastTimeRangeEnd;
    private float _lastSemitones;

    // The stretched frames not played yet, the two being played, and how far playback is between them.
    private float[] _stretched = [];
    private float[] _feedBuffer = [];
    private float[] _previous = [];
    private float[] _next = [];
    private int _stretchedPosition;
    private int _stretchedCount;
    private double _between;
    private bool _started;

    // The input format is supplied when the graph is built, without pulling upstream audio.
    public int ChannelCount { get; init; } = 2;

    public required IProperty<float> Semitones { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: false));

    public override AudioBuffer Flush(AudioProcessContext context)
        => RecordProcessedOutput(ProcessCore(context, draining: true));

    private bool IsUnshifted => Semitones.Value == 0 && !AnimationSampler.IsAnimated(Semitones);

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

        try
        {
            if (Prepare(context, draining))
                Play(output, context);

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
        if (_feed == null || _sampleRate != context.SampleRate)
        {
            _sampleRate = context.SampleRate;
            _feed = new WsolaFeed(_sampleRate, ChannelCount, static time => time);
            _stretched = new float[StretchedBlockFrames * ChannelCount];
            _feedBuffer = new float[FeedFrames * ChannelCount];
            _previous = new float[ChannelCount];
            _next = new float[ChannelCount];
            _lastTimeRangeEnd = null;
        }

        bool contiguous = _lastTimeRangeEnd is { } end && context.ContinuesFrom(end);
        if (draining)
        {
            // A drain continues where the last block ended; after a seek nothing is held.
            if (!contiguous)
                return false;

            if (!_feed.IsDraining)
            {
                // The input stops at the terminal, so the upstream's held tail starts right there, and what
                // the stretcher read ahead for the live audio has no part in it.
                Reset();
                _feed.BeginDrain(this);
            }

            _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;
            return true;
        }

        long? inputEnd = _feed.Stream.GetInputEnd(Inputs[0], context);
        if (!contiguous || _feed.InvalidatesReadAhead(inputEnd))
        {
            Reset();
            double start = context.TimeRange.Start.ToSeconds();
            _feed.Stream.Begin(start, start, forceReanchor: true);
        }

        _feed.InputEnd = inputEnd;
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;
        return true;
    }

    private void Reset()
    {
        _feed!.Reset();
        _stretchedPosition = 0;
        _stretchedCount = 0;
        _between = 0;
        _started = false;
    }

    private void Play(AudioBuffer output, AudioProcessContext context)
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
                if (_feed!.IsDraining)
                {
                    semitones[..chunk].Fill(_lastSemitones);
                }
                else
                {
                    context.AnimationSampler.SampleBuffer(
                        Semitones, context.GetChunkRange(processed, chunk), context.SampleRate, semitones[..chunk]);
                    _lastSemitones = semitones[chunk - 1];
                }

                PlayChunk(semitones[..chunk], output, processed, context);
                processed += chunk;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    private void PlayChunk(ReadOnlySpan<float> semitones, AudioBuffer output, int start, AudioProcessContext context)
    {
        Span<float> raw = output.GetRawSpan();
        int stride = output.SampleCount;
        float lastShift = float.NaN;
        double rate = 0;
        for (int i = 0; i < semitones.Length; i++)
        {
            // An expression or a hand-edited file can give any value, and playback reads this many frames per sample.
            float shift = float.IsNaN(semitones[i]) ? 0 : Math.Clamp(semitones[i], PitchShiftEffect.SemitonesMin, PitchShiftEffect.SemitonesMax);
            if (shift != lastShift)
            {
                lastShift = shift;
                rate = Math.Pow(2, shift / 12);
                // Stretched by the inverse of the playback rate, the source passes one sample for each one played.
                _feed!.Stretcher.Tempo = 1 / rate;
            }

            if (!_started)
            {
                ReadFrame(_previous, context);
                ReadFrame(_next, context);
                _started = true;
            }

            for (int ch = 0; ch < ChannelCount; ch++)
                raw[ch * stride + start + i] = _previous[ch] + (_next[ch] - _previous[ch]) * (float)_between;

            _between += rate;
            while (_between >= 1)
            {
                _between -= 1;
                (_previous, _next) = (_next, _previous);
                ReadFrame(_next, context);
            }
        }
    }

    // Past the end of the input the frame is silence.
    private void ReadFrame(float[] frame, AudioProcessContext context)
    {
        if (_stretchedPosition == _stretchedCount && !Refill(context))
        {
            Array.Clear(frame);
            return;
        }

        _stretched.AsSpan(_stretchedPosition * ChannelCount, ChannelCount).CopyTo(frame);
        _stretchedPosition++;
    }

    private bool Refill(AudioProcessContext context)
    {
        _stretchedPosition = 0;
        _stretchedCount = 0;
        while (true)
        {
            _stretchedCount = _feed!.Stretcher.ReceiveSamples(_stretched, StretchedBlockFrames);
            if (_stretchedCount > 0)
                return true;
            if (_feed.IsFlushed)
                return false;

            _feed.Feed(_feedBuffer, Inputs[0], context, _feed.IsDraining);
        }
    }
}
