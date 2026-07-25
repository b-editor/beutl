using Microsoft.Extensions.Logging;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Shared scaffolding for dynamics processors that derive one linked gain per sample from the peak
/// across all channels and apply it to every channel.
/// </summary>
/// <remarks>
/// Covers chunk validation, session/seek state resets, the per-channel buffer view cache, parameter
/// sanitization and the warn-once diagnostics. Derived nodes supply the parameter set and the gain
/// math. <c>LimiterNode</c> deliberately does not derive from this: its lookahead delay lines and
/// per-parameter error-severity diagnostics are a different contract, not a variation of this one.
/// </remarks>
public abstract class DynamicsNode : AudioNode
{
    protected const float MinDb = -100f;

    // Cached per-channel buffer handles so the hot loops skip GetChannelData's checks/re-slicing;
    // arrays reused across Process(), reallocated only on a channel-count change.
    private Memory<float>[]? _inputChannelCache;
    private Memory<float>[]? _outputChannelCache;

    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    private bool _loggedNonFiniteSample;
    // Per-parameter so a fault on one parameter does not suppress diagnostics for another.
    private readonly HashSet<string> _loggedNonFiniteParameters = new();
    // Separate from the non-finite latch so clamp and non-finite faults are not conflated.
    private readonly HashSet<string> _loggedClampedParameters = new();

    // Test-only counters (via InternalsVisibleTo) of warnings actually emitted, letting the latch
    // re-arm semantics be asserted without a logger sink.
    internal int NonFiniteSampleWarnings;
    internal int ClampWarnings;

    /// <summary>
    /// Logger for this node type. Implement with a <c>static readonly</c> field so one logger is
    /// shared per node type rather than allocated per instance.
    /// </summary>
    protected abstract ILogger Logger { get; }

    /// <summary>
    /// Human-readable node name used in exception and diagnostic messages (e.g. "Gate").
    /// </summary>
    protected abstract string DiagnosticName { get; }

    /// <summary>
    /// Whether any parameter carries a keyframe animation and processing must therefore run per-sample.
    /// </summary>
    protected abstract bool HasAnimatedParameters { get; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException(
                $"{DiagnosticName} node requires exactly one input but got {Inputs.Count}.");

        // Every path emits a fresh buffer (no pass-through), so dispose the consumed input.
        using var input = Inputs[0].Process(context);

        // Coefficients come from context.SampleRate while the output carries input.SampleRate, so a
        // mismatch would mislabel samples; this node does not resample.
        if (input.SampleRate != context.SampleRate)
            throw new InvalidOperationException(
                $"{DiagnosticName} node: sample rate mismatch. context={context.SampleRate}, input={input.SampleRate}.");

        // A sample-rate change needs new coefficients, so treat it as a full session boundary and
        // re-arm the diagnostics too.
        if (_lastSampleRate != context.SampleRate)
        {
            Reset();
            _lastSampleRate = context.SampleRate;
        }

        // Returns before _lastTimeRangeEnd advances: an empty chunk would otherwise mask a discontinuity.
        if (input.SampleCount == 0)
        {
            return new AudioBuffer(input.SampleRate, input.ChannelCount, 0);
        }

        // Nodes are cached across Compose() calls, so stale DSP state must not survive a seek or
        // restart. Diagnostics latches are deliberately kept — re-arming them on every scrub would let
        // a persistent fault re-log once per Process call.
        if (!_lastTimeRangeEnd.HasValue || !context.ContinuesFrom(_lastTimeRangeEnd.Value))
        {
            ResetDspState();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        return HasAnimatedParameters ? ProcessAnimated(input, context) : ProcessStatic(input, context);
    }

    protected abstract AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context);

    protected abstract AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context);

    /// <summary>
    /// Clears the DSP state (envelope / gain followers, hold counters) without touching diagnostics.
    /// </summary>
    protected abstract void ResetDspState();

    /// <summary>
    /// Resets this node to a clean "new render session" state: clears the DSP state and re-arms the
    /// one-shot diagnostic warnings.
    /// </summary>
    /// <remarks>
    /// Do not call mid-buffer during playback — dropping the follower there clicks. This is for genuine
    /// session boundaries (a deliberate re-render or an orchestrator-driven stop/seek);
    /// <see cref="Process"/> already handles sample-rate changes and time-range discontinuities.
    /// </remarks>
    public void Reset()
    {
        ResetDspState();
        ResetDiagnostics();
    }

    /// <summary>
    /// Re-arms the one-shot diagnostic warnings. Override to also re-arm latches a derived node adds,
    /// calling <c>base.ResetDiagnostics()</c>.
    /// </summary>
    protected virtual void ResetDiagnostics()
    {
        _loggedNonFiniteSample = false;
        _loggedNonFiniteParameters.Clear();
        _loggedClampedParameters.Clear();
    }

    /// <summary>
    /// Caches per-channel <see cref="Memory{T}"/> handles for the input and output buffers.
    /// </summary>
    /// <remarks>
    /// The backing arrays are reused across calls (reallocated only on a channel-count change) so the
    /// hot loops avoid per-sample GetChannelData. <see cref="Memory{T}"/> rather than <see cref="Span{T}"/>
    /// because a ref struct cannot live in an array.
    /// </remarks>
    protected (Memory<float>[] Inputs, Memory<float>[] Outputs) MapChannels(AudioBuffer input, AudioBuffer output)
    {
        int channels = input.ChannelCount;
        if (_inputChannelCache is null || _inputChannelCache.Length != channels)
        {
            _inputChannelCache = new Memory<float>[channels];
            _outputChannelCache = new Memory<float>[channels];
        }

        for (int ch = 0; ch < channels; ch++)
        {
            _inputChannelCache[ch] = input.GetChannelMemory(ch);
            _outputChannelCache![ch] = output.GetChannelMemory(ch);
        }

        return (_inputChannelCache, _outputChannelCache!);
    }

    /// <summary>
    /// Substitutes <paramref name="fallback"/> for a non-finite value, then clamps to
    /// [<paramref name="min"/>, <paramref name="max"/>].
    /// </summary>
    /// <remarks>
    /// The clamp stops an animated value (e.g. an Attack of 1e9 ms) from bypassing the declared
    /// <c>[Range]</c> and freezing the follower. A finite-but-out-of-range value is a different
    /// authoring error from a non-finite one, so each gets its own once-per-parameter warning.
    /// </remarks>
    protected float Sanitize(float value, float fallback, float declaredDefault, float min, float max, string paramName)
    {
        float safe = SafeParameter(value, fallback, declaredDefault, paramName);
        float clamped = Math.Clamp(safe, min, max);
        if (clamped != safe && _loggedClampedParameters.Add(paramName))
        {
            ClampWarnings++;
            Logger.LogWarning(
                "{Node} parameter '{Param}' value {Value} is outside its valid range [{Min}, {Max}]; clamping to {Clamped}. Further out-of-range occurrences for this parameter will be suppressed.",
                DiagnosticName, paramName, safe, min, max, clamped);
        }
        return clamped;
    }

    private float SafeParameter(float value, float fallback, float declaredDefault, string paramName)
    {
        if (float.IsFinite(value)) return value;
        // IProperty implementations may expose non-finite defaults, which Math.Clamp cannot recover.
        float safeFallback = float.IsFinite(fallback) ? fallback : declaredDefault;
        if (_loggedNonFiniteParameters.Add(paramName))
        {
            Logger.LogWarning(
                "{Node} parameter '{Param}' produced a non-finite value; falling back to {Fallback}. Further occurrences for this parameter will be suppressed.",
                DiagnosticName, paramName, safeFallback);
        }
        return safeFallback;
    }

    /// <summary>
    /// Replaces a non-finite output sample with 0 so it cannot propagate downstream.
    /// </summary>
    protected float SanitizeOutput(float sample)
    {
        if (float.IsFinite(sample)) return sample;
        if (!_loggedNonFiniteSample)
        {
            NonFiniteSampleWarnings++;
            Logger.LogWarning(
                "{Node} encountered a non-finite (NaN/Infinity) sample — with all parameters clamped this almost certainly originates upstream — and replaced it with 0 to protect downstream nodes. Further occurrences will be suppressed.",
                DiagnosticName);
            _loggedNonFiniteSample = true;
        }
        return 0f;
    }

    /// <summary>
    /// One-pole IIR smoothing coefficient whose 1/e settling time is <paramref name="timeMs"/>.
    /// </summary>
    /// <remarks>
    /// Applied in the dB domain, which tracks perceived loudness better than the linear one.
    /// </remarks>
    protected static float ComputeCoeff(float timeMs, int sampleRate)
    {
        return MathF.Exp(-1f / (timeMs * 0.001f * sampleRate));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drop the cached handles so we do not pin the last buffers' pooled memory after disposal;
            // they are re-filled on the next Process() call.
            _inputChannelCache = null;
            _outputChannelCache = null;
        }

        base.Dispose(disposing);
    }
}
