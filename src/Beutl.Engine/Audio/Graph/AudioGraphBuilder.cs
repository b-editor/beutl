using System;
using System.Collections.Generic;
using System.Linq;

namespace Beutl.Audio.Graph;

public sealed class AudioGraphBuilder
{
    private readonly List<AudioNode> _nodes = new();
    private AudioNode? _outputNode;
    private bool _built;

    public T AddNode<T>(T node) where T : AudioNode
    {
        ArgumentNullException.ThrowIfNull(node);
        
        if (_built)
            throw new InvalidOperationException("Cannot add nodes after the graph has been built.");
        
        if (_nodes.Contains(node))
            throw new InvalidOperationException("Node has already been added to this builder.");
        
        _nodes.Add(node);
        return node;
    }

    public void Connect(AudioNode from, AudioNode to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        
        if (_built)
            throw new InvalidOperationException("Cannot connect nodes after the graph has been built.");
        
        if (!_nodes.Contains(from))
            throw new InvalidOperationException("Source node must be added to the builder first.");
        
        if (!_nodes.Contains(to))
            throw new InvalidOperationException("Destination node must be added to the builder first.");
        
        // Check for cycles
        if (WouldCreateCycle(from, to))
            throw new InvalidOperationException("Connection would create a cycle in the graph.");
        
        to.AddInput(from);
    }

    public void SetOutput(AudioNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        
        if (_built)
            throw new InvalidOperationException("Cannot set output after the graph has been built.");
        
        if (!_nodes.Contains(node))
            throw new InvalidOperationException("Output node must be added to the builder first.");
        
        _outputNode = node;
    }

    public AudioGraph Build()
    {
        if (_built)
            throw new InvalidOperationException("Graph has already been built.");
        
        if (_outputNode == null)
            throw new InvalidOperationException("Output node must be set before building.");
        
        // Perform topological sort
        var sorted = TopologicalSort();
        
        _built = true;
        return new AudioGraph(_outputNode, sorted);
    }

    private bool WouldCreateCycle(AudioNode from, AudioNode to)
    {
        // Use DFS to check if 'from' is reachable from 'to'
        var visited = new HashSet<AudioNode>();
        var stack = new Stack<AudioNode>();
        
        stack.Push(to);
        
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            
            if (current == from)
                return true;
            
            if (visited.Contains(current))
                continue;
            
            visited.Add(current);
            
            foreach (var input in current.Inputs)
            {
                stack.Push(input);
            }
        }
        
        return false;
    }

    private List<AudioNode> TopologicalSort()
    {
        var result = new List<AudioNode>();
        var visited = new HashSet<AudioNode>();
        var visiting = new HashSet<AudioNode>();
        
        foreach (var node in _nodes)
        {
            if (!visited.Contains(node))
            {
                VisitNode(node, visited, visiting, result);
            }
        }
        
        return result;
    }

    private void VisitNode(AudioNode node, HashSet<AudioNode> visited, HashSet<AudioNode> visiting, List<AudioNode> result)
    {
        if (visiting.Contains(node))
            throw new InvalidOperationException("Cycle detected in the audio graph.");
        
        if (visited.Contains(node))
            return;
        
        visiting.Add(node);
        
        foreach (var input in node.Inputs)
        {
            VisitNode(input, visited, visiting, result);
        }
        
        visiting.Remove(node);
        visited.Add(node);
        result.Add(node);
    }
}