using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Beutl.Audio.Graph;

/// <summary>
/// Provides a context for building audio processing graphs, similar to GraphicsContext2D pattern.
/// </summary>
public sealed class AudioContext : IDisposable
{
    private readonly List<AudioNode> _nodes = new();
    private readonly Dictionary<AudioNode, List<AudioNode>> _connections = new();
    private readonly HashSet<AudioNode> _outputNodes = new();
    private AudioNode? _currentNode;
    private bool _disposed;

    /// <summary>
    /// Gets the sample rate for the audio context.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Gets the channel count for the audio context.
    /// </summary>
    public int ChannelCount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioContext"/> class.
    /// </summary>
    /// <param name="sampleRate">The sample rate for the audio context.</param>
    /// <param name="channelCount">The channel count for the audio context.</param>
    public AudioContext(int sampleRate, int channelCount)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (channelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelCount), "Channel count must be positive.");

        SampleRate = sampleRate;
        ChannelCount = channelCount;
    }

    /// <summary>
    /// Adds a node to the context.
    /// </summary>
    /// <param name="node">The node to add.</param>
    /// <returns>The added node.</returns>
    public T AddNode<T>(T node) where T : AudioNode
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!_nodes.Contains(node))
        {
            _nodes.Add(node);
            _connections[node] = new List<AudioNode>();
        }

        _currentNode = node;
        return node;
    }

    /// <summary>
    /// Creates and adds a source node to the context.
    /// </summary>
    /// <param name="source">The sound source.</param>
    /// <returns>The created source node.</returns>
    public SourceNode CreateSourceNode(ISoundSource source)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        var node = new SourceNode(source);
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a gain node to the context.
    /// </summary>
    /// <param name="gain">The gain value.</param>
    /// <returns>The created gain node.</returns>
    public GainNode CreateGainNode(float gain = 1.0f)
    {
        ThrowIfDisposed();

        var node = new GainNode(gain);
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a mixer node to the context.
    /// </summary>
    /// <returns>The created mixer node.</returns>
    public MixerNode CreateMixerNode()
    {
        ThrowIfDisposed();

        var node = new MixerNode(ChannelCount);
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds an effect node to the context.
    /// </summary>
    /// <param name="effect">The audio effect.</param>
    /// <returns>The created effect node.</returns>
    public EffectNode CreateEffectNode(IAudioEffect effect)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(effect, nameof(effect));

        var node = new EffectNode(effect);
        return AddNode(node);
    }

    /// <summary>
    /// Connects the current node to another node.
    /// </summary>
    /// <param name="destination">The destination node.</param>
    /// <returns>The destination node.</returns>
    public T ConnectTo<T>(T destination) where T : AudioNode
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination, nameof(destination));

        if (_currentNode == null)
            throw new InvalidOperationException("No current node to connect from. Add a node first.");

        Connect(_currentNode, destination);
        _currentNode = destination;
        return destination;
    }

    /// <summary>
    /// Connects two nodes.
    /// </summary>
    /// <param name="source">The source node.</param>
    /// <param name="destination">The destination node.</param>
    public void Connect(AudioNode source, AudioNode destination)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentNullException.ThrowIfNull(destination, nameof(destination));

        if (source == destination)
            throw new ArgumentException("Cannot connect a node to itself.");

        if (!_nodes.Contains(source))
            AddNode(source);
        if (!_nodes.Contains(destination))
            AddNode(destination);

        destination.AddInput(source);
        _connections[source].Add(destination);

        // Remove from output nodes if it now has a connection
        _outputNodes.Remove(source);
    }

    /// <summary>
    /// Marks a node as an output node.
    /// </summary>
    /// <param name="node">The node to mark as output.</param>
    public void MarkAsOutput(AudioNode node)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!_nodes.Contains(node))
            AddNode(node);

        _outputNodes.Add(node);
    }

    /// <summary>
    /// Sets the current node for chaining operations.
    /// </summary>
    /// <param name="node">The node to set as current.</param>
    /// <returns>The node.</returns>
    public T SetCurrent<T>(T node) where T : AudioNode
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!_nodes.Contains(node))
            throw new ArgumentException("Node must be added to the context first.", nameof(node));

        _currentNode = node;
        return node;
    }

    /// <summary>
    /// Builds an audio graph from the current context.
    /// </summary>
    /// <returns>The built audio graph.</returns>
    public AudioGraph BuildGraph()
    {
        ThrowIfDisposed();

        // Determine output nodes
        var outputs = _outputNodes.Count > 0 
            ? _outputNodes.ToList() 
            : _nodes.Where(node => !_connections.Values.Any(list => list.Contains(node))).ToList();

        if (outputs.Count == 0)
            throw new InvalidOperationException("No output nodes found in the graph.");

        // If multiple outputs, create a mixer
        AudioNode finalOutput;
        if (outputs.Count == 1)
        {
            finalOutput = outputs[0];
        }
        else
        {
            var mixer = new MixerNode(ChannelCount);
            foreach (var output in outputs)
            {
                mixer.AddInput(output);
            }
            finalOutput = mixer;
        }

        // Build the graph
        var builder = new AudioGraphBuilder();
        
        // Add all connections
        foreach (var (source, destinations) in _connections)
        {
            foreach (var destination in destinations)
            {
                builder.Connect(source, destination);
            }
        }

        return builder.Build(finalOutput);
    }

    /// <summary>
    /// Clears all nodes and connections from the context.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();

        foreach (var node in _nodes)
        {
            node.Dispose();
        }

        _nodes.Clear();
        _connections.Clear();
        _outputNodes.Clear();
        _currentNode = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioContext));
    }
}