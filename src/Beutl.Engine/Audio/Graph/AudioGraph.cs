using System;
using System.Collections.Generic;
using Beutl.Audio.Graph.Exceptions;

namespace Beutl.Audio.Graph;

public sealed class AudioGraph : IDisposable
{
    private readonly AudioNode _outputNode;
    private readonly List<AudioNode> _sortedNodes;
    private bool _disposed;

    internal AudioGraph(AudioNode outputNode, List<AudioNode> sortedNodes)
    {
        _outputNode = outputNode ?? throw new ArgumentNullException(nameof(outputNode));
        _sortedNodes = sortedNodes ?? throw new ArgumentNullException(nameof(sortedNodes));
    }

    public AudioNode OutputNode => _outputNode;
    
    public IReadOnlyList<AudioNode> Nodes => _sortedNodes;

    public AudioBuffer Process(AudioProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            // Clear all cached outputs
            ClearCaches();
            
            // Process the output node (which will recursively process its inputs)
            return _outputNode.Process(context);
        }
        catch (Exception ex)
        {
            // Clear caches on error to prevent inconsistent state
            ClearCaches();
            throw new AudioGraphException("Error processing audio graph.", ex);
        }
    }

    public void ClearCaches()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        foreach (var node in _sortedNodes)
        {
            node.ClearCache();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var node in _sortedNodes)
            {
                try
                {
                    node.Dispose();
                }
                catch
                {
                    // Ignore disposal errors to prevent cascading failures
                }
            }
            
            _sortedNodes.Clear();
            _disposed = true;
        }
    }
}

