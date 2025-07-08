using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Beutl.Animation;
using Beutl.Audio.Graph.Effects;
using Beutl.Audio.Graph.Nodes;
using Beutl.Media.Source;

namespace Beutl.Audio.Graph;

/// <summary>
/// Provides a context for building audio processing graphs, similar to GraphicsContext2D pattern.
/// </summary>
public sealed class AudioContext : IDisposable
{
    private readonly List<AudioNode> _nodes = new();
    private readonly Dictionary<AudioNode, List<AudioNode>> _connections = new();
    private readonly HashSet<AudioNode> _outputNodes = new();
    private List<AudioNode>? _previousNodes;
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
    /// Initializes a new instance with previous nodes for differential update.
    /// </summary>
    public AudioContext(int sampleRate, int channelCount, IEnumerable<AudioNode> previousNodes)
        : this(sampleRate, channelCount)
    {
        _previousNodes = previousNodes.ToList();
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
        else
        {
            // If reusing a node, ensure its connections list exists
            if (!_connections.ContainsKey(node))
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

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<SourceNode>()
                .FirstOrDefault(n => n.Source == source);
            if (existing != null)
            {
                _previousNodes.Remove(existing);
                return AddNode(existing);
            }
        }

        var node = new SourceNode { Source = source };
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a gain node to the context.
    /// </summary>
    /// <param name="gain">The gain value.</param>
    /// <param name="target">The target object for animation binding (optional).</param>
    /// <param name="gainProperty">The property to bind for animated gain (optional).</param>
    /// <returns>The created gain node.</returns>
    public GainNode CreateGainNode(float gain = 1.0f, IAnimatable? target = null, CoreProperty<float>? gainProperty = null)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(gain, nameof(gain));

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<GainNode>()
                .FirstOrDefault(n => n.Target == target && n.GainProperty == gainProperty);
            if (existing != null)
            {
                _previousNodes.Remove(existing);
                existing.StaticGain = gain;
                return AddNode(existing);
            }
        }

        var node = new GainNode
        {
            StaticGain = gain,
            Target = target,
            GainProperty = gainProperty
        };
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a shift node to the context.
    /// </summary>
    /// <param name="shift">The time shift duration.</param>
    /// <returns>The created shift node.</returns>
    public ShiftNode CreateShiftNode(TimeSpan shift)
    {
        ThrowIfDisposed();
        if(shift < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shift), "Shift must be non-negative.");

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<ShiftNode>()
                .FirstOrDefault(n => n.Shift==shift);
            if (existing != null)
            {
                _previousNodes.Remove(existing);
                return AddNode(existing);
            }
        }

        var node = new ShiftNode
        {
            Shift = shift
        };
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a clip node to the context.
    /// </summary>
    /// <param name="start">The start time of the clip.</param>
    /// <param name="duration">The duration of the clip.</param>
    public ClipNode CreateClipNode(TimeSpan start, TimeSpan duration)
    {
        ThrowIfDisposed();
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<ClipNode>()
                .FirstOrDefault(n => n.Duration == duration && n.Start == start);
            if (existing != null)
            {
                _previousNodes.Remove(existing);
                return AddNode(existing);
            }
        }

        var node = new ClipNode
        {
            Start = start,
            Duration = duration
        };
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a mixer node to the context.
    /// </summary>
    /// <returns>The created mixer node.</returns>
    public MixerNode CreateMixerNode()
    {
        ThrowIfDisposed();

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<MixerNode>().FirstOrDefault();
            if (existing != null)
            {
                _previousNodes.Remove(existing);
                existing.ClearInputs();
                return AddNode(existing);
            }
        }

        var node = new MixerNode();
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

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<EffectNode>()
                .FirstOrDefault(n => n.Effect == effect);
            if (existing != null)
            {
                _previousNodes.Remove(existing);
                return AddNode(existing);
            }
        }

        var node = new EffectNode { Effect = effect };
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
            var mixer = new MixerNode();
            foreach (var output in outputs)
            {
                mixer.AddInput(output);
            }
            finalOutput = mixer;
        }

        // Build the graph
        var builder = new AudioGraphBuilder();

        // Add all nodes to the builder
        foreach (var node in _nodes)
        {
            builder.AddNode(node);
        }

        // Add all connections
        foreach (var (source, destinations) in _connections)
        {
            foreach (var destination in destinations)
            {
                builder.Connect(source, destination);
            }
        }

        // Set the output node
        builder.SetOutput(finalOutput);

        return builder.Build();
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

    /// <summary>
    /// Gets whether this context has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets the list of nodes in this context.
    /// </summary>
    public IReadOnlyList<AudioNode> Nodes => _nodes;

    /// <summary>
    /// Clears all connections while keeping nodes.
    /// </summary>
    public void ClearConnections()
    {
        ThrowIfDisposed();

        // Clear connections only
        foreach (var list in _connections.Values)
        {
            list.Clear();
        }

        foreach (var node in _nodes)
        {
            node.ClearInputs();
        }

        _outputNodes.Clear();
        _currentNode = null;
    }

    /// <summary>
    /// Removes a specific node from the context.
    /// </summary>
    /// <param name="node">The node to remove.</param>
    public void RemoveNode(AudioNode node)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!_nodes.Contains(node))
            return;

        // Remove from all connections
        _connections.Remove(node);
        foreach (var list in _connections.Values)
        {
            list.Remove(node);
        }

        // Remove from other tracking
        _outputNodes.Remove(node);
        if (_currentNode == node)
            _currentNode = null;

        // Clear inputs of other nodes that reference this one
        foreach (var otherNode in _nodes)
        {
            otherNode.RemoveInput(node);
        }

        // Finally remove from nodes list
        _nodes.Remove(node);
    }

    /// <summary>
    /// Begins a differential update session.
    /// </summary>
    public void BeginUpdate(IEnumerable<AudioNode> previousNodes)
    {
        ThrowIfDisposed();

        // Save previous nodes for reuse
        _previousNodes = previousNodes.ToList();

        // Clear current state
        _nodes.Clear();
        _connections.Clear();
        _outputNodes.Clear();
        _currentNode = null;
    }

    /// <summary>
    /// Completes a differential update session.
    /// </summary>
    public void EndUpdate()
    {
        ThrowIfDisposed();

        // Dispose unused nodes from previous state
        if (_previousNodes != null)
        {
            foreach (var prevNode in _previousNodes)
            {
                if (!_nodes.Contains(prevNode))
                {
                    prevNode.Dispose();
                }
            }
            _previousNodes = null;
        }
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
