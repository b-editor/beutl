using Microsoft.Extensions.Logging;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Shared scaffolding for dynamics processors that derive one linked gain per sample from the peak
/// across all channels and apply it to every channel.
/// </summary>
/// <remarks>
/// Derived nodes supply parameter sampling and gain math. <c>LimiterNode</c> remains separate because
/// its lookahead delay lines and error-severity diagnostics require a different lifecycle.
/// </remarks>
public abstract class DynamicsNode : AudioNode
{
    protected const float MinDb = -100f;

    // Cache channel views across Process calls to avoid repeated slicing in hot loops.
    private Memory<float>[]? _inputChannelCache;
    private Memory<float>[]? _outputChannelCache;

    private int _lastSampleRate;
    private TimeSpan? _lastTimeRangeEnd;

    private bool _loggedNonFiniteSample;
    private readonly HashSet<string> _loggedNonFiniteParameters = new();
    private readonly HashSet<string> _loggedClampedParameters = new();

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

        return ProcessTail(Inputs[0].Process(context), context);
    }

    protected override AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context)
    {
        bool ownsInput = true;
        try
        {
            // This node does not resample; mismatched rates would produce mislabeled output.
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

            // Reset DSP state across seeks without re-arming warnings for persistent faults.
            if (!_lastTimeRangeEnd.HasValue || !context.ContinuesFrom(_lastTimeRangeEnd.Value))
            {
                ResetDspState();
            }

            AudioBuffer output = HasAnimatedParameters
                ? ProcessAnimated(input, context)
                : ProcessStatic(input, context);

            // A chunk that threw must not look contiguous, or the next one inherits half-mutated state.
            _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

            // If a hook returns the input, ownership transfers to the caller.
            ownsInput = !ReferenceEquals(output, input);
            return output;
        }
        catch
        {
            _lastTimeRangeEnd = null;
            throw;
        }
        finally
        {
            if (ownsInput)
            {
                input.Dispose();
            }
        }
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
    /// Backing arrays are reused across calls. <see cref="Memory{T}"/> is required because ref structs
    /// cannot live in arrays.
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
    /// Folds one channel's sample into the running linked peak, ignoring non-finite values.
    /// </summary>
    /// <remarks>
    /// Infinity would become the shared peak and alter every channel's gain, while
    /// <see cref="SanitizeOutput"/> only zeroes the corrupt channel.
    /// </remarks>
    protected static float AccumulatePeak(float peak, float sample)
    {
        float abs = MathF.Abs(sample);
        return float.IsFinite(abs) && abs > peak ? abs : peak;
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
            // Do not pin the last buffers' pooled memory after disposal.
            _inputChannelCache = null;
            _outputChannelCache = null;
        }

        base.Dispose(disposing);
    }
}
