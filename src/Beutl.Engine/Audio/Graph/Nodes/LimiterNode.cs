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
    // Upper bound on the per-chunk animation-sampling scratch: AnimationChunkSize ×
    // number-of-animated-parameters × sizeof(float). With four parameters today this caps the
    // stack scratch at 16 KiB per ProcessAnimated call (smaller buffers allocate proportionally
    // less) — well within a thread's default stack budget while still amortizing the per-chunk
    // animation sampling overhead.
    private const int AnimationChunkSize = 1024;
    private const long TimestampQuantizationToleranceTicks = 1;

    private static readonly ILogger s_logger = Log.CreateLogger<LimiterNode>();

    private CircularBuffer<float>[]? _delayLines;
    private CircularBuffer<float>? _peakBuffer;
    private int _maxLookaheadSamples;
    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;
    private float _currentGain = 1f;

    // O(1)-amortized sliding-window maximum for the static path. A monotonic-decreasing deque
    // (ring buffer over peak positions) replaces the per-sample O(lookahead) rescan. It persists
    // across contiguous chunks like the delay line, is rebuilt from _peakBuffer only when the
    // lookahead length changes (rare), and is cleared on Reset/format change. The animated path,
    // where the lookahead can vary per sample, keeps the direct rescan (ScanWindowPeak).
    private float[]? _dqVal;
    private long[]? _dqPos;
    private int _dqMask;
    private int _dqHead;
    private int _dqCount;
    private int _dequeLookahead = -1;
    private long _globalPos;

    // Latched warning flags — keep audio-rate logging from spamming the sink. Per-parameter
    // latches so that, for example, a non-finite Threshold does not silence a subsequent
    // non-finite Release. Cleared only on full re-initialization (sample-rate/channel-count
    // change) — chunk discontinuities (seek/loop/edit) intentionally do NOT re-arm them, or
    // a persistent upstream defect would log every chunk.
    private bool _warnedNonFiniteThreshold;
    private bool _warnedNonFiniteRelease;
    private bool _warnedNonFiniteLookahead;
    private bool _warnedNonFiniteMakeup;
    private bool _warnedNonFiniteInputSample;
    private bool _warnedEmptyChunk;

    public required IProperty<float> Threshold { get; init; }

    public required IProperty<float> Release { get; init; }

    public required IProperty<float> Lookahead { get; init; }

    public required IProperty<float> MakeupGain { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException(
                $"LimiterNode requires exactly one input but has {Inputs.Count}.");

        var input = Inputs[0].Process(context)
            ?? throw new InvalidOperationException("LimiterNode: upstream Process returned null.");

        if (input.SampleRate != context.SampleRate)
            throw new InvalidOperationException(
                $"LimiterNode: sample rate mismatch. context={context.SampleRate}, input={input.SampleRate}.");

        if (input.SampleCount == 0)
        {
            // Empty chunks are not produced by the normal scheduling path (GetSampleCount uses
            // Math.Ceiling so only TimeRange.Duration == Zero reaches here). Treat them as a
            // notable upstream event and log once-per-format-change. Crucially, do NOT touch
            // _lastTimeRangeEnd: an empty chunk processes no audio, so the next non-empty chunk
            // must be evaluated against the previous *non-empty* chunk's end. Updating it here
            // would silently mask a discontinuity when the empty chunk happens to land at a
            // different position than the previous chunk's end.
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

        // The node instance is cached and reused across chunks. When the next chunk does not
        // continue from the previous one (seek, loop, edit, restart) we must drop the delay line
        // and gain state — otherwise audio from the previous segment would leak into the first
        // lookahead-window worth of output samples. IsTimestampContiguous tolerates only the
        // one-tick rounding error introduced by independently quantized TimeSpan boundaries.
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

        // Update only on success: if Process throws mid-buffer (realistically only from
        // AnimationSampler on a later inner chunk of ProcessAnimated, after earlier samples were
        // already ingested), _currentGain and the delay line may be partially mutated. No production
        // caller re-invokes Process with an identical context after a throw — Composer,
        // SampleProviderImpl, and PlayerViewModel all propagate the exception and stop — so the
        // same-chunk-retry branch below is a latent/defensive consideration, not an active path.
        // For completeness, the next call resolves as:
        //  - Contiguous throw (no Reset() ran this call): _lastTimeRangeEnd retains the previous
        //    end. A next call at a different Start hits the discontinuity branch, which Reset()s and
        //    discards the partial state. A next call at the same Start (a hypothetical same-chunk
        //    retry that no current caller performs) would instead inherit the partial state; this is
        //    tolerated rather than guarded, because the alternative — always Reset() after a throw —
        //    would needlessly discard correct delay-line state and no caller actually retries.
        //  - Throw after Reset() ran: _lastTimeRangeEnd was already cleared to null by Reset(),
        //    so the next call hits the `!HasValue` branch and Reset()s again before processing,
        //    discarding any partial state.
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        return output;
    }

    private static bool IsTimestampContiguous(TimeSpan previousEnd, TimeSpan nextStart)
    {
        // Independently rounded TimeSpan sample boundaries can differ by one tick even when the
        // underlying sample indices are adjacent. A two-tick difference remains a real seek/edit
        // boundary and must reset the delay line.
        long difference = nextStart.Ticks - previousEnd.Ticks;
        return difference is >= -TimestampQuantizationToleranceTicks and <= TimestampQuantizationToleranceTicks;
    }

    private void InitializeBuffers(int sampleRate, int channelCount)
    {
        // Tear the previous state down first and null the fields immediately so a throw inside
        // the construction loop below cannot leave us referencing half-initialized buffers.
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
        // so the requested length must be strictly greater than the maximum lookaheadSamples we
        // will ever clamp to (MaxLookaheadMs · sampleRate). The buffer rounds this
        // up to the next power of two internally — the +1 is for the read-bounds check, not the
        // rounding.
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

        // Sliding-window-max deque capacity must exceed the largest possible window (max elements)
        // by two slots: a push transiently holds up to (window + 1) entries before the out-of-window
        // eviction runs, so the ring must hold at least max + 2 entries to never overwrite its own
        // front. Round that up to a power of two so the per-sample ring wrapping in PushWindowPeak
        // is a mask (& _dqMask) instead of an integer division, mirroring CircularBuffer<T>.
        int dqCap = (int)BitOperations.RoundUpToPowerOf2((uint)(max + 2));
        _dqVal = new float[dqCap];
        _dqPos = new long[dqCap];
        _dqMask = dqCap - 1;
        _dqHead = 0;
        _dqCount = 0;
        _dequeLookahead = -1;
        _globalPos = 0;

        // Format change is the one moment where the previous warning history is unrelated to
        // the new state, so re-arm so that a persistent upstream defect surfaces once per
        // format change rather than literally never logging again after the first hit.
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

    // Math.Clamp does not coerce NaN nor ±Infinity to the bounds — both would poison
    // _currentGain permanently if they slipped through. Substituting a safe fallback here keeps
    // the DSP stable; the caller logs once per parameter at error severity so an upstream
    // animation/binding bug is visible in production logs (Sentry-grade) without spamming.
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
        // Threshold falls back to the *default* (-1 dB) rather than MaxThresholdDb (0 dB). Using
        // 0 dB here would silently disable limiting for the rest of the session — exactly the
        // silent-failure mode this guard is meant to prevent. -1 dB still limits while keeping
        // the substitution audible (peaks just under unity) so the issue surfaces.
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

        // Lookahead is constant for the whole call, so the window maximum can be tracked with an
        // O(1)-amortized monotonic deque instead of rescanning the window every sample. Rebuild it
        // only when the lookahead length differs from what the deque currently tracks (first call
        // after a reset, or a static parameter edit on a reused node).
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

        return output;
    }

    private AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        // Per-chunk animation-sampling scratch, sized to the actual work (capped at
        // AnimationChunkSize) so a small buffer pays a proportionally small stack cost, matching the
        // sibling nodes (CompressorNode/DelayNode/GainNode). The inner loop's chunkSize never exceeds
        // this. Worst case is four AnimationChunkSize-float spans = 16 KiB, well within the stack budget.
        int scratchSize = Math.Min(AnimationChunkSize, input.SampleCount);
        Span<float> thresholds = stackalloc float[scratchSize];
        Span<float> releases = stackalloc float[scratchSize];
        Span<float> lookaheads = stackalloc float[scratchSize];
        Span<float> makeups = stackalloc float[scratchSize];

        // Lookahead can change per sample here, which the monotonic deque cannot track without
        // unbounded history, so this path uses the direct window rescan. Invalidate the deque so a
        // later static chunk on the same (non-reset) node rebuilds it from the retained peak ring.
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
                // Derive recomputes one Exp + two Pow per sample here. That per-sample transcendental
                // cost is a deliberate trade-off for sample-accurate parameter automation; the common
                // no-animation case takes ProcessStatic, which derives the coefficients once per call.
                var c = Derive(thresholds[i], releases[i], lookaheads[i], makeups[i], context.SampleRate);
                int idx = processed + i;
                // The animated path ignores IngestSample's returned per-sample peak (only its
                // side-effects on the delay line and peak ring matter); the window maximum comes from
                // ScanWindowPeak over the ring. The static path, by contrast, feeds it into PushWindowPeak.
                _ = IngestSample(inRaw, sampleCount, channelCount, idx);
                float windowPeak = ScanWindowPeak(c.LookaheadSamples);
                EmitSample(outRaw, sampleCount, channelCount, idx, windowPeak, c.ThresholdLin, c.MakeupLin, c.LookaheadSamples, c.ReleaseCoef);
            }

            processed += chunkSize;
        }

        return output;
    }

    // Reads one input sample per channel, coerces non-finite values, feeds the delay lines and the
    // peak-detection ring, advances the global sample position, and returns the channel-linked peak.
    // inRaw is the channel-major backing span (channel ch sample i lives at ch * sampleCount + i),
    // fetched once per call so the hot loop avoids AudioBuffer.GetChannelData's per-sample
    // disposed/bounds/Slice overhead.
    //
    // Channel-linked peak detection: take max(|s_ch|) across all channels so that a single shared
    // gain is applied to every channel — preserves inter-channel phase.
    //
    // NaN/Infinity input samples are coerced to 0 here. Without this guard:
    //   - NaN written into the delay line passes straight through to the output.
    //   - Inf forces currentPeak → Inf, then targetGain = thresholdLin / Inf = 0,
    //     and finally `delayed * 0` = `Inf * 0` = NaN once the gain reduction kicks in.
    // We log at most once per format change so an upstream bug surfaces without flooding the sink
    // across every chunk discontinuity.
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
                    // Error severity matches the non-finite-parameter path in ClampFinite: a NaN/Inf
                    // in the audio stream is an upstream DSP defect that would corrupt output, so it
                    // is surfaced at the same level rather than as a quieter warning.
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

    // Applies the gain envelope for one output sample and writes it to every channel. outRaw is the
    // channel-major backing span (see IngestSample).
    //
    // With non-zero lookahead the reduction is applied before the offending sample reaches the
    // output, so a hard attack stays transparent. With lookahead=0 this degrades to a
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
    // just-written peak and Read(lookaheadSamples) is the peak that the sample currently exiting the
    // delay line is about to face, so the reduction is in place before that peak arrives. Used by the
    // animated path where the window length varies per sample.
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

    // O(1)-amortized sliding-window maximum: pushes the just-written peak (at position _globalPos-1)
    // onto the monotonic-decreasing deque, evicts entries older than the window
    // [pos - lookaheadSamples, pos], and returns the current maximum (the deque front). Equivalent to
    // ScanWindowPeak for a constant lookahead. Requires EnsureDeque(lookaheadSamples) first.
    private float PushWindowPeak(float value, int lookaheadSamples)
    {
        int mask = _dqMask;
        long pos = _globalPos - 1;

        while (_dqCount > 0 && _dqVal![(_dqHead + _dqCount - 1) & mask] <= value)
            _dqCount--;

        // After the monotonic back-eviction the deque holds at most the in-window count =
        // lookaheadSamples + 1 <= _maxLookaheadSamples <= cap - 2, so a free slot for this push is
        // guaranteed (_dqCount < cap, i.e. tail != _dqHead). Assert BEFORE the write so a future change
        // that breaks the bound is caught before it overwrites the front, not one step after the
        // corruption (Debug-only; compiled out of Release).
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
    // lookahead. No-op when the deque already tracks this length. After a reset _globalPos is 0 and
    // the peak history is empty, so the deque simply starts empty; when the lookahead changes on a
    // reused node the candidate set is reconstructed from the retained peak ring (O(lookahead) once).
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
            // Iterate the window oldest-to-newest (Read(j) is the peak j samples back) so the deque
            // ends up ordered front=oldest with values monotonically decreasing front-to-back.
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
    /// Clears the per-channel delay lines and the peak-detection buffer, resets the internal
    /// gain to unity, and clears the cached chunk timestamp so the next Process() call does
    /// not consider itself contiguous. Process() invokes this automatically on chunk
    /// discontinuity, so external callers do not normally need to call it. Per-parameter
    /// non-finite warning latches are intentionally NOT cleared here — see InitializeBuffers
    /// for the spam-protection rationale.
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

        // Discard the sliding-window deque along with the peak history it tracks. _globalPos restarts
        // at 0 so positions stay aligned with the freshly-cleared _peakBuffer, and the lookahead
        // marker is invalidated so the next static chunk rebuilds the deque from scratch.
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
