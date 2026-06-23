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
    /// Drains the latency this node and its <see cref="Inputs"/> still hold, as the
    /// <paramref name="context"/>-sized block that follows the clip's last <see cref="Process"/> output.
    /// The real source is treated as exhausted — a node with no inputs returns silence — so the only
    /// non-silent content is what delay lines / lookahead buffers release; that is why a trimmed clip
    /// cannot bleed real audio here. Callers must invoke it immediately after the terminal
    /// <see cref="Process"/> with a context that abuts it, so the cached node sees no discontinuity.
    /// The default passes a single input's drain through unchanged; latency-bearing nodes override.
    /// </summary>
    public virtual AudioBuffer Flush(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_inputs.Count == 1)
            return _inputs[0].Flush(context);

        return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
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
