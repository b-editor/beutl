using System;
using System.Collections.Generic;
using System.Linq;
using Beutl.Audio.Graph.Exceptions;

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
            throw new AudioGraphBuildException("Cannot add nodes after the graph has been built.");
        
        if (_nodes.Contains(node))
            throw new AudioGraphBuildException("Node has already been added to this builder.");
        
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
        
        // First add the connection
        to.AddInput(from);
        
        // Then check for cycles after the connection is made
        if (HasCycle())
        {
            // Remove the connection if it creates a cycle
            to.RemoveInput(from);
            throw new InvalidOperationException("Connection would create a cycle in the graph.");
        }
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

    private bool HasCycle()
    {
        // Use DFS with recursion stack to detect cycles
        var visited = new HashSet<AudioNode>();
        var recursionStack = new HashSet<AudioNode>();
        
        foreach (var node in _nodes)
        {
            if (HasCycleDFS(node, visited, recursionStack))
                return true;
        }
        
        return false;
    }
    
    private bool HasCycleDFS(AudioNode node, HashSet<AudioNode> visited, HashSet<AudioNode> recursionStack)
    {
        if (recursionStack.Contains(node))
            return true;
        
        if (visited.Contains(node))
            return false;
        
        visited.Add(node);
        recursionStack.Add(node);
        
        // Check all nodes that this node outputs to
        foreach (var otherNode in _nodes)
        {
            if (otherNode.Inputs.Contains(node))
            {
                if (HasCycleDFS(otherNode, visited, recursionStack))
                    return true;
            }
        }
        
        recursionStack.Remove(node);
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