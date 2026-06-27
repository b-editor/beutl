namespace Beutl.Audio.Graph;

public abstract class AudioNode : IDisposable
{
    private readonly List<AudioNode> _inputs = new();
    private bool _disposed;

    public IReadOnlyList<AudioNode> Inputs => _inputs;

    public void AddInput(AudioNode input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_inputs.Contains(input))
            return;

        _inputs.Add(input);
    }

    public void RemoveInput(AudioNode input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _inputs.Remove(input);
    }

    public void ClearInputs()
    {
        _inputs.Clear();
    }

    public abstract AudioBuffer Process(AudioProcessContext context);

    /// <summary>
    /// Applies this node's own processing to an already-produced <paramref name="input"/> buffer
    /// instead of pulling <see cref="Inputs"/>[0] itself. <see cref="Process"/> feeds it real upstream
    /// audio (<paramref name="draining"/> is <see langword="false"/>); <see cref="Flush"/> feeds it the
    /// drained tail (<paramref name="draining"/> is <see langword="true"/>), so a transforming node
    /// processes the tail the same way it processes the body. A node whose output geometry is driven by
    /// an animated parameter (a lookahead delay) must, while draining, hold that parameter at the value
    /// retained from the clip's terminal sample rather than re-sampling automation over the post-clip
    /// range — otherwise it reads the wrong tail. The default is pass-through (returns
    /// <paramref name="input"/> unchanged), keeping the zero-processing path byte-identical. A node that
    /// returns a fresh buffer takes ownership of <paramref name="input"/> and disposes it, exactly as its
    /// Process already does.
    /// </summary>
    protected virtual AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context, bool draining) => input;

    /// <summary>Channel layout this node's last <see cref="Process"/> emitted; the silent-flush
    /// fallback matches it so a flush never changes the channel count a downstream node just saw.</summary>
    protected int LastProcessedChannelCount { get; private set; } = 2;

    /// <summary>Records the channel count a <see cref="Process"/> override is about to emit, so
    /// <see cref="CreateSilentFlush"/> can mirror it. Leaf/source nodes call this from Process.
    /// Known limitation: a custom zero-input leaf that omits this call keeps the default stereo count,
    /// so a mono/multichannel leaf would flush stereo silence and make a downstream limiter reinitialize
    /// and drop its tail. Built-in <see cref="Nodes.SourceNode"/> records correctly; a wider contract
    /// (the base learning the emitted layout) is a follow-up.</summary>
    protected void RecordProcessedChannelCount(int channelCount) => LastProcessedChannelCount = channelCount;

    /// <summary>The silence a node with no live source emits when flushed; sized to the last processed
    /// channel layout. Override for a node whose flush silence needs a different shape.</summary>
    protected virtual AudioBuffer CreateSilentFlush(AudioProcessContext context)
        => new(context.SampleRate, LastProcessedChannelCount, context.GetSampleCount());

    /// <summary>
    /// Drains the latency this node and its <see cref="Inputs"/> still hold, as the
    /// <paramref name="context"/>-sized block that follows the clip's last <see cref="Process"/> output.
    /// The real source is treated as exhausted — a node with no inputs returns silence — so the only
    /// non-silent content is what delay lines / lookahead buffers release; that is why a trimmed clip
    /// cannot bleed real audio here. Callers must invoke it immediately after the terminal
    /// <see cref="Process"/> with a context that abuts it, so the cached node sees no discontinuity.
    /// A single-input node runs its upstream's drain through its own <see cref="ProcessTail"/>, so
    /// downstream effects still shape the tail; a fan-in node must override to drain and merge branches.
    /// </summary>
    public virtual AudioBuffer Flush(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_inputs.Count == 0)
            return CreateSilentFlush(context);

        if (_inputs.Count == 1)
        {
            AudioBuffer drained = _inputs[0].Flush(context);
            AudioBuffer result;
            try
            {
                result = ProcessTail(drained, context, draining: true);
            }
            catch
            {
                // ProcessTail threw before taking ownership; dispose the drain we pulled (Dispose is
                // idempotent, so a transforming node that already consumed it is unaffected).
                drained.Dispose();
                throw;
            }

            // Pass-through ProcessTail hands back the same instance, which we must not dispose since we
            // return it; a transforming ProcessTail already consumed `drained` and returns a fresh one.
            if (!ReferenceEquals(result, drained))
                drained.Dispose();

            return result;
        }

        throw new InvalidOperationException(
            $"{GetType().Name} has {_inputs.Count} inputs; override Flush to drain and merge them.");
    }

    /// <summary>
    /// Reports the processing latency this node alone introduces at <paramref name="sampleRate"/>, in
    /// samples (a lookahead/delay-line node returns the samples its output lags its input; pass-through
    /// nodes return 0). Report-only: it never affects <see cref="Process"/> output. Pass the output
    /// (post-resample) sample rate, since latency is rate-dependent.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    public virtual int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return 0;
    }

    /// <summary>
    /// Latency accumulated along the path feeding this node's output: this node's own latency plus the
    /// largest total latency among its <see cref="Inputs"/>. A single-input cascade therefore sums,
    /// while a fan-in node (a mixer) takes the slowest branch — the alignment a compensator would use.
    /// Override to impose a different upstream fold (e.g. a weighted-sum mixer). Requires an acyclic
    /// input graph, the same precondition <see cref="Process"/> already relies on.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    public virtual int GetTotalLatencySamples(int sampleRate)
    {
        // Guard at the entry point, not just via the GetLatencySamples call below: an override may fold
        // the upstream recursion before reaching it, so the contract has to hold here too.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int upstream = 0;
        foreach (AudioNode input in _inputs)
        {
            int total = input.GetTotalLatencySamples(sampleRate);
            if (total > upstream)
                upstream = total;
        }

        return GetLatencySamples(sampleRate) + upstream;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _inputs.Clear();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
