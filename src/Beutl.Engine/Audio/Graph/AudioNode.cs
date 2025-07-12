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
