using System;
using System.Collections.Generic;

namespace Beutl.Audio.Graph;

public abstract class AudioNode : IDisposable
{
    private readonly List<AudioNode> _inputs = new();
    private bool _disposed;

    public IReadOnlyList<AudioNode> Inputs => _inputs;
    
    protected AudioBuffer? CachedOutput { get; set; }

    public void AddInput(AudioNode input)
    {
        ArgumentNullException.ThrowIfNull(input);
        
        if (_inputs.Contains(input))
            throw new InvalidOperationException("Input node already connected.");
        
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

    internal void ClearCache()
    {
        CachedOutput = null;
    }

    public abstract AudioBuffer Process(AudioProcessContext context);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                CachedOutput?.Dispose();
                CachedOutput = null;
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