using System.Diagnostics;
using System.Numerics;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.Audio.Effects.LimiterParameters;

namespace Beutl.Audio.Graph.Nodes;

public sealed class LimiterNode : AudioNode
{
    // Caps the per-chunk animation-sampling scratch at 4 parameters × 1024 floats = 16 KiB of
    // stackalloc per ProcessAnimated call, while still amortizing the per-chunk sampling overhead.
    private const int AnimationChunkSize = 1024;
    private const long TimestampQuantizationToleranceTicks = 1;

    private static readonly ILogger s_logger = Log.CreateLogger<LimiterNode>();

    private CircularBuffer<float>[]? _delayLines;
    private CircularBuffer<float>? _peakBuffer;
    private int _maxLookaheadSamples;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;
    private float _currentGain = 1f;

    // Monotonic-decreasing deque (ring buffer over peak positions) giving the static path an
    // O(1)-amortized sliding-window maximum instead of a per-sample O(lookahead) rescan. Persists
    // across contiguous chunks, rebuilt only when the lookahead length changes, cleared on
    // Reset/format change. The animated path varies lookahead per sample and uses ScanWindowPeak.
    private float[]? _dqVal;
    private long[]? _dqPos;
    private int _dqMask;
    private int _dqHead;
    private int _dqCount;
    private int _dequeLookahead = -1;
    private long _globalPos;

    // Per-parameter latches that keep audio-rate logging from spamming the sink (one latch each so
    // a non-finite Threshold doesn't mask a non-finite Release). Cleared only on full
    // re-initialization, not on chunk discontinuity — otherwise a persistent defect logs every chunk.
    private bool _warnedNonFiniteThreshold;
    private bool _warnedNonFiniteRelease;
    private bool _warnedNonFiniteLookahead;
    private bool _warnedNonFiniteMakeup;
    private bool _warnedNonFiniteInputSample;
    private bool _warnedEmptyChunk;

    // Test-only counter (via InternalsVisibleTo) of output buffers disposed after Process fails.
    internal int OutputBuffersDisposedAfterFailure;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Lookahead { get; init; }

    public required IProperty<float> MakeupGain { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException(
                $"LimiterNode requires exactly one input but has {Inputs.Count}.");

        // Every path emits a fresh buffer (no pass-through), so dispose the consumed input.
        using var input = Inputs[0].Process(context)
            ?? throw new InvalidOperationException("LimiterNode: upstream Process returned null.");

        if (input.SampleRate != context.SampleRate)
            throw new InvalidOperationException(
                $"LimiterNode: sample rate mismatch. context={context.SampleRate}, input={input.SampleRate}.");

        if (input.SampleCount == 0)
        {
            // Empty chunks only reach here when TimeRange.Duration == Zero, so log once as a
            // notable upstream event. Do NOT touch _lastTimeRangeEnd: an empty chunk processes no
            // audio, so the next non-empty chunk must be evaluated against the previous *non-empty*
            // chunk's end — updating it here would mask a discontinuity.
            if (!_warnedEmptyChunk)
            {
                s_logger.LogDebug(
                    "LimiterNode: received empty input chunk at {Range}; passing through.",
                    context.TimeRange);
                _warnedEmptyChunk = true;
            }

            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);
        }

        if (_delayLines == null || _peakBuffer == null
            || _lastSampleRate != context.SampleRate
            || _delayLines.Length != input.ChannelCount)
        {
            InitializeBuffers(context.SampleRate, input.ChannelCount);
            _lastSampleRate = context.SampleRate;
        }

        // The node is reused across chunks. When the next chunk doesn't continue from the previous
        // one (seek, loop, edit, restart), drop the delay line and gain state — otherwise the
        // previous segment leaks into the first lookahead window of output.
        if (!_lastTimeRangeEnd.HasValue || !IsTimestampContiguous(_lastTimeRangeEnd.Value, context.TimeRange.Start))
        {
            if (_lastTimeRangeEnd.HasValue)
            {
                s_logger.LogDebug(
                    "LimiterNode: chunk discontinuity (last end={Last}, new start={Start}); resetting state.",
                    _lastTimeRangeEnd, context.TimeRange.Start);
            }

            Reset();
        }

        bool hasAnimation = Threshold.Animation != null
                            || Release.Animation != null
                            || Lookahead.Animation != null
                            || MakeupGain.Animation != null;

        var output = hasAnimation
            ? ProcessAnimated(input, context)
            : ProcessStatic(input, context);

        // Update only on success. If Process throws mid-buffer the state is left partially mutated,
        // but no production caller retries the same chunk: a next call at a different Start hits the
        // discontinuity branch and Reset()s, and a throw after Reset() already cleared
        // _lastTimeRangeEnd so the next call Reset()s again. A same-Start retry would inherit the
        // partial state; that's tolerated rather than guarded, since always resetting after a throw
        // would needlessly discard correct delay-line state no caller actually retries.
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        return output;
    }

    private static bool IsTimestampContiguous(TimeSpan previousEnd, TimeSpan nextStart)
    {
        // Adjacent sample boundaries can differ by one tick from independent TimeSpan rounding; a
        // two-tick difference is a real seek/edit boundary and must reset the delay line.
        long difference = nextStart.Ticks - previousEnd.Ticks;
        return difference is >= -TimestampQuantizationToleranceTicks and <= TimestampQuantizationToleranceTicks;
    }

    private void InitializeBuffers(int sampleRate, int channelCount)
    {
        // Null the fields up front so a throw in the construction loop below can't leave us
        // referencing half-initialized buffers.
        if (_delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Dispose();
            }

            _delayLines = null;
        }

        _peakBuffer?.Dispose();
        _peakBuffer = null;

        // +1 because CircularBuffer.Read(samplesBack) returns silence when samplesBack >= length,
        // so length must exceed the maximum lookaheadSamples we ever clamp to (MaxLookaheadMs ·
        // sampleRate). The buffer rounds up to a power of two internally; the +1 is for the
        // read-bounds check, not the rounding.
        int max = Math.Max(1, (int)(MaxLookaheadMs / 1000f * sampleRate) + 1);

        var lines = new CircularBuffer<float>[channelCount];
        try
        {
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = new CircularBuffer<float>(max);
            }

            _peakBuffer = new CircularBuffer<float>(max);
        }
        catch
        {
            foreach (var l in lines)
            {
                l?.Dispose();
            }

            _peakBuffer?.Dispose();
            _peakBuffer = null;
            throw;
        }

        _maxLookaheadSamples = max;
        _delayLines = lines;

        // The deque capacity needs max + 2 entries: a push transiently holds (window + 1) entries
        // before the out-of-window eviction runs, so the ring must never overwrite its own front.
        // Round up to a power of two so PushWindowPeak's ring wrapping is a mask, not a division.
        int dqCap = (int)BitOperations.RoundUpToPowerOf2((uint)(max + 2));
        _dqVal = new float[dqCap];
        _dqPos = new long[dqCap];
        _dqMask = dqCap - 1;
        _dqHead = 0;
        _dqCount = 0;
        _dequeLookahead = -1;
        _globalPos = 0;

        // A format change makes the previous warning history irrelevant, so re-arm the latches:
        // a persistent upstream defect should surface once per format change, not just once ever.
        _currentGain = 1f;
        _warnedNonFiniteThreshold = false;
        _warnedNonFiniteRelease = false;
        _warnedNonFiniteLookahead = false;
        _warnedNonFiniteMakeup = false;
        _warnedNonFiniteInputSample = false;
        _warnedEmptyChunk = false;

        s_logger.LogDebug(
            "LimiterNode: initialized buffers (sampleRate={SampleRate}, channels={Channels}, maxLookaheadSamples={MaxLookahead}).",
            sampleRate, channelCount, max);
    }

    // Math.Clamp does not coerce NaN/±Infinity to the bounds, and either would permanently poison
    // _currentGain. Substituting a safe fallback keeps the DSP stable; the first hit per parameter
    // is logged at error severity so an upstream animation/binding bug stays visible.
    private float ClampFinite(float value, float min, float max, float fallback, string parameterName, ref bool warned)
    {
        if (float.IsFinite(value))
            return Math.Clamp(value, min, max);

        if (!warned)
        {
            s_logger.LogError(
                "LimiterNode: non-finite {Parameter}={Value}; substituting {Fallback}. " +
                "Likely an animation/binding bug upstream — limiter behavior will not match user settings.",
                parameterName, value, fallback);
            warned = true;
        }

        return fallback;
    }

    private readonly record struct DerivedCoefficients(
        float ThresholdLin,
        float MakeupLin,
        int LookaheadSamples,
        float ReleaseCoef);

    private DerivedCoefficients Derive(float thresholdDbRaw, float releaseMsRaw, float lookaheadMsRaw, float makeupDbRaw, int sampleRate)
    {
        // Threshold falls back to the default (-1 dB), not MaxThresholdDb (0 dB): 0 dB would
        // silently disable limiting for the rest of the session — the exact silent failure this
        // guard prevents. -1 dB still limits, peaking just under unity so the issue stays audible.
        float thresholdDb = ClampFinite(thresholdDbRaw, MinThresholdDb, MaxThresholdDb, DefaultThresholdDb, nameof(Threshold), ref _warnedNonFiniteThreshold);
        float releaseMs = ClampFinite(releaseMsRaw, MinReleaseMs, MaxReleaseMs, MinReleaseMs, nameof(Release), ref _warnedNonFiniteRelease);
        float lookaheadMs = ClampFinite(lookaheadMsRaw, MinLookaheadMs, MaxLookaheadMs, MinLookaheadMs, nameof(Lookahead), ref _warnedNonFiniteLookahead);
        float makeupDb = ClampFinite(makeupDbRaw, MinMakeupGainDb, MaxMakeupGainDb, 0f, nameof(MakeupGain), ref _warnedNonFiniteMakeup);

        return new DerivedCoefficients(
            ThresholdLin: AudioMath.ConvertDbToLinear(thresholdDb),
            MakeupLin: AudioMath.ConvertDbToLinear(makeupDb),
            LookaheadSamples: Math.Clamp((int)(lookaheadMs / 1000f * sampleRate), 0, _maxLookaheadSamples - 1),
            ReleaseCoef: MathF.Exp(-1f / (releaseMs * 0.001f * sampleRate)));
    }

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        var c = Derive(Threshold.CurrentValue, Release.CurrentValue, Lookahead.CurrentValue, MakeupGain.CurrentValue, context.SampleRate);

        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        bool succeeded = false;
        try
        {
            // Lookahead is constant for the whole call, so the window maximum comes from the
            // O(1)-amortized deque; rebuild it only when the tracked lookahead length changes.
            EnsureDeque(c.LookaheadSamples);

            int channelCount = _delayLines!.Length;
            int sampleCount = input.SampleCount;
            ReadOnlySpan<float> inRaw = input.GetRawSpan();
            Span<float> outRaw = output.GetRawSpan();

            for (int i = 0; i < sampleCount; i++)
            {
                float currentPeak = IngestSample(inRaw, sampleCount, channelCount, i);
                float windowPeak = PushWindowPeak(currentPeak, c.LookaheadSamples);
                EmitSample(outRaw, sampleCount, channelCount, i, windowPeak, c.ThresholdLin, c.MakeupLin, c.LookaheadSamples, c.ReleaseCoef);
            }

            succeeded = true;
            return output;
        }
        finally
        {
            if (!succeeded)
            {
                output.Dispose();
                OutputBuffersDisposedAfterFailure++;
            }
        }
    }

    private AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);
        bool succeeded = false;
        try
        {
            // Per-chunk animation-sampling scratch, sized to the actual work (capped at
            // AnimationChunkSize) so small buffers pay a proportionally small stack cost, matching the
            // sibling nodes (CompressorNode/DelayNode/GainNode).
            int scratchSize = Math.Min(AnimationChunkSize, input.SampleCount);
            Span<float> thresholds = stackalloc float[scratchSize];
            Span<float> releases = stackalloc float[scratchSize];
            Span<float> lookaheads = stackalloc float[scratchSize];
            Span<float> makeups = stackalloc float[scratchSize];

            // Lookahead can vary per sample here, which the deque cannot track, so this path rescans
            // directly. Invalidate the deque so a later static chunk on the same node rebuilds it.
            _dequeLookahead = -1;

            int channelCount = _delayLines!.Length;
            int sampleCount = input.SampleCount;
            ReadOnlySpan<float> inRaw = input.GetRawSpan();
            Span<float> outRaw = output.GetRawSpan();

            int processed = 0;
            while (processed < sampleCount)
            {
                int chunkSize = Math.Min(AnimationChunkSize, sampleCount - processed);

                var chunkStart = context.GetTimeForSample(processed);
                var chunkEnd = context.GetTimeForSample(processed + chunkSize);
                var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

                context.AnimationSampler.SampleBuffer(Threshold, chunkRange, context.SampleRate, thresholds[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Release, chunkRange, context.SampleRate, releases[..chunkSize]);
                context.AnimationSampler.SampleBuffer(Lookahead, chunkRange, context.SampleRate, lookaheads[..chunkSize]);
                context.AnimationSampler.SampleBuffer(MakeupGain, chunkRange, context.SampleRate, makeups[..chunkSize]);

                for (int i = 0; i < chunkSize; i++)
                {
                    // Derive recomputes one Exp + two Pow per sample — the deliberate cost of
                    // sample-accurate automation. The common no-animation case takes ProcessStatic,
                    // which derives the coefficients once per call.
                    var c = Derive(thresholds[i], releases[i], lookaheads[i], makeups[i], context.SampleRate);
                    int idx = processed + i;
                    // The window maximum comes from ScanWindowPeak over the ring, so IngestSample's
                    // returned peak is ignored here (only its delay-line/peak-ring side-effects matter).
                    _ = IngestSample(inRaw, sampleCount, channelCount, idx);
                    float windowPeak = ScanWindowPeak(c.LookaheadSamples);
                    EmitSample(outRaw, sampleCount, channelCount, idx, windowPeak, c.ThresholdLin, c.MakeupLin, c.LookaheadSamples, c.ReleaseCoef);
                }

                processed += chunkSize;
            }

            succeeded = true;
            return output;
        }
        finally
        {
            if (!succeeded)
            {
                output.Dispose();
                OutputBuffersDisposedAfterFailure++;
            }
        }
    }

    // Reads one input sample per channel, coerces non-finite values, feeds the delay lines and the
    // peak-detection ring, advances the global position, and returns the channel-linked peak.
    // inRaw is the channel-major backing span (channel ch sample i at ch * sampleCount + i),
    // passed in so the hot loop avoids AudioBuffer.GetChannelData's per-sample overhead.
    //
    // The peak is max(|s_ch|) across channels, so one shared gain applies to every channel and
    // inter-channel phase is preserved. NaN/Inf samples are coerced to 0 — otherwise NaN passes
    // through the delay line and Inf drives targetGain to 0, turning `Inf * 0` into NaN.
    private float IngestSample(ReadOnlySpan<float> inRaw, int sampleCount, int channelCount, int sampleIndex)
    {
        float currentPeak = 0f;
        for (int ch = 0; ch < channelCount; ch++)
        {
            float s = inRaw[ch * sampleCount + sampleIndex];
            if (!float.IsFinite(s))
            {
                if (!_warnedNonFiniteInputSample)
                {
                    // Error severity, matching ClampFinite: a NaN/Inf in the stream is an upstream
                    // DSP defect that would corrupt output.
                    s_logger.LogError(
                        "LimiterNode: non-finite input sample at channel={Channel}, index={Index}, value={Value}. " +
                        "Likely an upstream DSP defect corrupting the audio stream; coercing to 0.",
                        ch, sampleIndex, s);
                    _warnedNonFiniteInputSample = true;
                }

                s = 0f;
            }

            float abs = MathF.Abs(s);
            if (abs > currentPeak)
                currentPeak = abs;

            _delayLines![ch].Write(s);
        }

        _peakBuffer!.Write(currentPeak);
        _globalPos++;
        return currentPeak;
    }

    // Applies the gain envelope for one output sample and writes it to every channel (outRaw is the
    // channel-major backing span, see IngestSample). With non-zero lookahead the reduction lands
    // before the offending sample reaches the output; with lookahead=0 it degrades to a
    // hard-clipper-style limiter (still correct, just less transparent).
    private void EmitSample(
        Span<float> outRaw,
        int sampleCount,
        int channelCount,
        int sampleIndex,
        float windowPeak,
        float thresholdLin,
        float makeupLin,
        int lookaheadSamples,
        float releaseCoef)
    {
        float targetGain;
        if (windowPeak > thresholdLin && windowPeak > 0f)
        {
            targetGain = thresholdLin / windowPeak;
        }
        else
        {
            targetGain = 1f;
        }

        if (targetGain < _currentGain)
        {
            _currentGain = targetGain;
        }
        else
        {
            _currentGain = targetGain + (_currentGain - targetGain) * releaseCoef;
        }

        float finalGain = _currentGain * makeupLin;

        for (int ch = 0; ch < channelCount; ch++)
        {
            float delayed = _delayLines![ch].Read(lookaheadSamples);
            outRaw[ch * sampleCount + sampleIndex] = delayed * finalGain;
        }
    }

    // Direct O(lookahead) window maximum over the peak ring [0..lookaheadSamples]. Read(0) is the
    // just-written peak; Read(lookaheadSamples) is the peak the sample now exiting the delay line is
    // about to face, so reduction lands before it. Used by the animated path (per-sample window).
    private float ScanWindowPeak(int lookaheadSamples)
    {
        float windowPeak = 0f;
        int windowSize = lookaheadSamples + 1;
        for (int j = 0; j < windowSize; j++)
        {
            float v = _peakBuffer!.Read(j);
            if (v > windowPeak)
                windowPeak = v;
        }

        return windowPeak;
    }

    // O(1)-amortized sliding-window maximum: pushes the just-written peak (at _globalPos-1) onto the
    // monotonic deque, evicts entries older than [pos - lookaheadSamples, pos], and returns the
    // front (the max). Equivalent to ScanWindowPeak for constant lookahead; needs EnsureDeque first.
    private float PushWindowPeak(float value, int lookaheadSamples)
    {
        int mask = _dqMask;
        long pos = _globalPos - 1;

        while (_dqCount > 0 && _dqVal![(_dqHead + _dqCount - 1) & mask] <= value)
            _dqCount--;

        // After the back-eviction the deque holds at most lookaheadSamples + 1 <= cap - 2 entries,
        // so a free slot is guaranteed. Assert before the write so a future change that breaks the
        // bound is caught before it overwrites the front (Debug-only).
        Debug.Assert(_dqCount < _dqVal!.Length, "LimiterNode deque overflow: no free slot, front would be overwritten.");

        int tail = (_dqHead + _dqCount) & mask;
        _dqVal[tail] = value;
        _dqPos![tail] = pos;
        _dqCount++;

        long windowStart = pos - lookaheadSamples;
        while (_dqPos[_dqHead] < windowStart)
        {
            _dqHead = (_dqHead + 1) & mask;
            _dqCount--;
        }

        return _dqVal[_dqHead];
    }

    // (Re)builds the monotonic deque so PushWindowPeak can continue incrementally for the given
    // lookahead. No-op when it already tracks this length. After a reset the deque starts empty;
    // when the lookahead changes on a reused node it is rebuilt from the retained peak ring once.
    private void EnsureDeque(int lookaheadSamples)
    {
        if (_dequeLookahead == lookaheadSamples)
            return;

        _dqHead = 0;
        _dqCount = 0;

        int mask = _dqMask;
        long last = _globalPos - 1;
        if (last >= 0)
        {
            int span = (int)Math.Min(lookaheadSamples + 1L, last + 1L);
            // Iterate oldest-to-newest (Read(j) is the peak j samples back) so the deque ends up
            // front=oldest with values monotonically decreasing front-to-back.
            for (int j = span - 1; j >= 0; j--)
            {
                float v = _peakBuffer!.Read(j);
                long pos = last - j;

                while (_dqCount > 0 && _dqVal![(_dqHead + _dqCount - 1) & mask] <= v)
                    _dqCount--;

                int tail = (_dqHead + _dqCount) & mask;
                _dqVal![tail] = v;
                _dqPos![tail] = pos;
                _dqCount++;
            }
        }

        _dequeLookahead = lookaheadSamples;
    }

    /// <summary>
    /// Clears the delay lines and peak buffer, resets the gain to unity, and clears the cached
    /// chunk timestamp so the next Process() call is not treated as contiguous. Process() calls
    /// this automatically on a chunk discontinuity, so external callers rarely need it. The
    /// non-finite warning latches are intentionally NOT cleared here (see InitializeBuffers).
    /// </summary>
    public void Reset()
    {
        if (_delayLines != null)
        {
            foreach (var line in _delayLines)
            {
                line.Clear();
            }
        }

        _peakBuffer?.Clear();
        _currentGain = 1f;
        _lastTimeRangeEnd = null;

        // Discard the deque. _globalPos restarts at 0 to stay aligned with the cleared _peakBuffer,
        // and the lookahead marker is invalidated so the next static chunk rebuilds from scratch.
        _dqHead = 0;
        _dqCount = 0;
        _dequeLookahead = -1;
        _globalPos = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_delayLines != null)
            {
                foreach (var line in _delayLines)
                {
                    line.Dispose();
                }

                _delayLines = null;
            }

            _peakBuffer?.Dispose();
            _peakBuffer = null;
        }

        base.Dispose(disposing);
    }
}
