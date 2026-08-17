using Beutl.Audio.Effects;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.Audio.Graph;

/// <summary>
/// Provides a context for building audio processing graphs, similar to GraphicsContext2D pattern.
/// </summary>
public sealed class AudioContext : IDisposable
{
    private readonly List<AudioNode> _nodes = new();
    private readonly Dictionary<AudioNode, List<AudioNode>> _connections =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AudioNode> _outputNodes = new(ReferenceEqualityComparer.Instance);
    private List<AudioNode>? _previousNodes;
    private AudioNode? _currentNode;
    private bool _disposed;
    private bool _topologyMutationInProgress;

    private sealed record InputSnapshot(AudioNode Node, AudioNode[] Inputs, object?[] States);

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
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!ContainsReference(_nodes, node))
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
    /// Creates a node with parameters, factory, updater, and comparer for differential updates.
    /// </summary>
    /// <typeparam name="TNode">Type of the node to create.</typeparam>
    /// <typeparam name="TParams">Type of the parameters for node creation and comparison.</typeparam>
    /// <param name="parameters">The parameters for node creation and comparison.</param>
    /// <param name="factory">The factory function to create a new node if needed.</param>
    /// <param name="updater">The updater function to update an existing node if reused.</param>
    /// <param name="comparer">The comparer function to determine if an existing node can be reused based on the parameters.</param>
    /// <returns>The created or reused node.</returns>
    public TNode CreateNode<TNode, TParams>(
        TParams parameters,
        Func<TParams, TNode> factory,
        Action<TParams, TNode> updater,
        Func<TParams, TNode, bool> comparer)
        where TNode : AudioNode
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));
        ArgumentNullException.ThrowIfNull(updater, nameof(updater));
        ArgumentNullException.ThrowIfNull(comparer, nameof(comparer));

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<TNode>()
                .FirstOrDefault(n => comparer(parameters, n));
            if (existing != null)
            {
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
                updater(parameters, existing);
                return AddNode(existing);
            }
        }

        var node = factory(parameters);
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a source node to the context.
    /// </summary>
    /// <param name="source">The sound source resource.</param>
    /// <returns>The created source node.</returns>
    public SourceNode CreateSourceNode(SoundSource.Resource source)
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(source);

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<SourceNode>()
                .FirstOrDefault(n => source.Compare(n.Source));
            if (existing != null)
            {
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
                return AddNode(existing);
            }
        }

        var node = new SourceNode { Source = source.Capture() };
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a gain node to the context.
    /// </summary>
    /// <param name="gain">The gain value.</param>
    /// <param name="target">The target object for animation binding (optional).</param>
    /// <param name="gainProperty">The property to bind for animated gain (optional).</param>
    /// <returns>The created gain node.</returns>
    public GainNode CreateGainNode(IProperty<float> gain)
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(gain);

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<GainNode>()
                .FirstOrDefault(n => n.Gain == gain);
            if (existing != null)
            {
                // Matched by Gain reference, so existing.Gain already == gain; no re-assignment needed
                // (and Gain is now init-only).
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
                return AddNode(existing);
            }
        }

        var node = new GainNode
        {
            Gain = gain
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
        ThrowIfTopologyMutation();

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<ShiftNode>()
                .FirstOrDefault(n => n.Shift == shift);
            if (existing != null)
            {
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
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
        ThrowIfTopologyMutation();
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<ClipNode>()
                .FirstOrDefault(n => n.Duration == duration && n.Start == start);
            if (existing != null)
            {
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
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
        ThrowIfTopologyMutation();

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<MixerNode>().FirstOrDefault();
            if (existing != null)
            {
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
                return AddNode(existing);
            }
        }

        var node = new MixerNode();
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a resample node to the context.
    /// </summary>
    /// <param name="sourceSampleRate">The source sample rate for resampling.</param>
    /// <returns>The created resample node.</returns>
    public ResampleNode CreateResampleNode(int sourceSampleRate)
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();
        if (sourceSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSampleRate), "Source sample rate must be positive.");

        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<ResampleNode>()
                .FirstOrDefault(n => n.SourceSampleRate == sourceSampleRate);
            if (existing != null)
            {
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
                return AddNode(existing);
            }
        }

        var node = new ResampleNode { SourceSampleRate = sourceSampleRate };
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a speed node to the context.
    /// </summary>
    /// <param name="speed">The playback speed multiplier (100.0 = normal speed).</param>
    /// <param name="target">The target object for animation binding (optional).</param>
    /// <param name="speedProperty">The property to bind for animated speed (optional).</param>
    /// <returns>The created speed node.</returns>
    public SpeedNode CreateSpeedNode(IProperty<float> speed)
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(speed);

        // Try to reuse from previous nodes
        if (_previousNodes != null)
        {
            var existing = _previousNodes.OfType<SpeedNode>()
                .FirstOrDefault(n => n.Speed == speed);
            if (existing != null)
            {
                RemoveReference(_previousNodes, existing);
                existing.ClearInputs();
                existing.Speed = speed;
                return AddNode(existing);
            }
        }

        var node = new SpeedNode
        {
            Speed = speed
        };
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
        ThrowIfTopologyMutation();
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
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentNullException.ThrowIfNull(destination, nameof(destination));

        if (ReferenceEquals(source, destination))
            throw new ArgumentException("Cannot connect a node to itself.");

        if (!ContainsReference(_nodes, source))
            AddNode(source);
        if (!ContainsReference(_nodes, destination))
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
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!ContainsReference(_nodes, node))
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
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!ContainsReference(_nodes, node))
            throw new ArgumentException("Node must be added to the context first.", nameof(node));

        _currentNode = node;
        return node;
    }

    /// <summary>
    /// Gets the output nodes in the context.
    /// </summary>
    /// <returns>The output nodes.</returns>
    public IEnumerable<AudioNode> GetOutputNodes()
    {
        ThrowIfDisposed();
        return _outputNodes;
    }

    /// <summary>
    /// Clears all nodes and connections from the context.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();

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
        ThrowIfTopologyMutation();

        _topologyMutationInProgress = true;
        try
        {
            ClearConnectionsCore();
        }
        finally
        {
            _topologyMutationInProgress = false;
        }
    }

    private void ClearConnectionsCore()
    {

        var snapshots = new List<InputSnapshot>(_nodes.Count);
        foreach (AudioNode node in _nodes)
        {
            AudioNode[] inputs = [.. node.Inputs];
            object?[] states = new object?[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                states[i] = node.CaptureInputStateForRollback(inputs[i], i);
            }

            snapshots.Add(new InputSnapshot(node, inputs, states));
        }

        foreach (InputSnapshot snapshot in snapshots)
        {
            snapshot.Node.BeginInputTopologyTransaction();
        }

        var cleared = new List<InputSnapshot>(_nodes.Count);
        try
        {
            foreach (InputSnapshot snapshot in snapshots)
            {
                snapshot.Node.ClearInputs();
                cleared.Add(snapshot);
                if (snapshot.Node.Inputs.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Audio connection clearing hooks must leave every node without inputs.");
                }
            }

            bool nodesUnchanged = _nodes.Count == snapshots.Count;
            if (nodesUnchanged)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    if (!ReferenceEquals(snapshots[i].Node, _nodes[i]))
                    {
                        nodesUnchanged = false;
                        break;
                    }
                }
            }

            if (!nodesUnchanged)
            {
                throw new InvalidOperationException(
                    "Audio connection clearing hooks must not mutate the context node set.");
            }

            foreach (InputSnapshot snapshot in snapshots)
            {
                if (snapshot.Node.Inputs.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Audio connection clearing hooks must leave every node without inputs.");
                }
            }

            foreach (InputSnapshot snapshot in snapshots)
            {
                snapshot.Node.CompleteInputTopologyCommit();
            }
        }
        catch (Exception clearException)
        {
            List<Exception>? rollbackFailures = null;
            for (int i = cleared.Count - 1; i >= 0; i--)
            {
                try
                {
                    RestoreInputs(cleared[i]);
                }
                catch (Exception rollbackException)
                {
                    (rollbackFailures ??= []).Add(rollbackException);
                }
            }

            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                try
                {
                    snapshots[i].Node.RollbackInputTopologyTransaction();
                }
                catch (Exception rollbackException)
                {
                    (rollbackFailures ??= []).Add(rollbackException);
                }
            }

            if (rollbackFailures is { Count: > 0 })
            {
                rollbackFailures.Insert(0, clearException);
                throw new AggregateException(
                    "Audio connection clearing failed and rollback encountered one or more errors.",
                    rollbackFailures);
            }

            throw;
        }

        foreach (var list in _connections.Values)
        {
            list.Clear();
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
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!ContainsReference(_nodes, node))
            return;

        _topologyMutationInProgress = true;
        try
        {
            RemoveNodeCore(node);
        }
        finally
        {
            _topologyMutationInProgress = false;
        }
    }

    private void RemoveNodeCore(AudioNode node)
    {
        // Remove dependent inputs first. The context bookkeeping is deliberately left untouched until
        // every node hook succeeds, so a failed hook leaves the graph available for a retry.
        var affected = _nodes
            .Where(otherNode => !ReferenceEquals(otherNode, node)
                && ContainsReference(otherNode.Inputs, node))
            .ToArray();
        foreach (AudioNode otherNode in affected)
        {
            otherNode.BeginInputTopologyTransaction();
        }

        var removedFrom = new List<(AudioNode Node, int Index, object? State)>();
        try
        {
            foreach (AudioNode otherNode in affected)
            {
                int index = IndexOfReference(otherNode.Inputs, node);
                object? state = otherNode.CaptureInputStateForRollback(node, index);
                otherNode.RemoveInput(node);
                removedFrom.Add((otherNode, index, state));
            }

            foreach (AudioNode otherNode in affected)
            {
                otherNode.CompleteInputTopologyCommit();
            }
        }
        catch (Exception removalException)
        {
            List<Exception>? rollbackFailures = null;
            for (int i = removedFrom.Count - 1; i >= 0; i--)
            {
                (AudioNode otherNode, int index, object? state) = removedFrom[i];
                try
                {
                    otherNode.RestoreInput(node, index);
                    otherNode.RestoreInputStateForRollback(node, index, state);
                }
                catch (Exception rollbackException)
                {
                    (rollbackFailures ??= []).Add(rollbackException);
                }
            }

            for (int i = affected.Length - 1; i >= 0; i--)
            {
                try
                {
                    affected[i].RollbackInputTopologyTransaction();
                }
                catch (Exception rollbackException)
                {
                    (rollbackFailures ??= []).Add(rollbackException);
                }
            }

            if (rollbackFailures is { Count: > 0 })
            {
                rollbackFailures.Insert(0, removalException);
                throw new AggregateException(
                    "Audio node removal failed and rollback encountered one or more errors.",
                    rollbackFailures);
            }

            throw;
        }

        _connections.Remove(node);
        foreach (var list in _connections.Values)
        {
            RemoveReference(list, node);
        }

        _outputNodes.Remove(node);
        if (ReferenceEquals(_currentNode, node))
            _currentNode = null;

        RemoveReference(_nodes, node);
    }

    /// <summary>
    /// Begins a differential update session.
    /// </summary>
    public void BeginUpdate(IEnumerable<AudioNode> previousNodes)
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();

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
        ThrowIfTopologyMutation();

        // Dispose unused nodes from previous state
        if (_previousNodes is { Count: > 0 })
        {
            foreach (var prevNode in _previousNodes)
            {
                if (!ContainsReference(_nodes, prevNode))
                {
                    foreach (AudioNode node in _nodes)
                    {
                        node.RemoveInput(prevNode);
                    }
                    prevNode.Dispose();
                }
            }

            _previousNodes = null;
        }
    }

    public void Dispose()
    {
        ThrowIfTopologyMutation();
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }

    private static bool ContainsReference(IReadOnlyList<AudioNode> nodes, AudioNode node)
    {
        return IndexOfReference(nodes, node) >= 0;
    }

    private static int IndexOfReference(IReadOnlyList<AudioNode> nodes, AudioNode node)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
                return i;
        }

        return -1;
    }

    private static bool RemoveReference(List<AudioNode> nodes, AudioNode node)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
            {
                nodes.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private static void RestoreInputs(InputSnapshot snapshot)
    {
        for (int i = snapshot.Node.Inputs.Count - 1; i >= 0; i--)
        {
            if (!ContainsReference(snapshot.Inputs, snapshot.Node.Inputs[i]))
                snapshot.Node.RemoveInput(snapshot.Node.Inputs[i]);
        }

        for (int i = 0; i < snapshot.Inputs.Length; i++)
        {
            AudioNode input = snapshot.Inputs[i];
            int currentIndex = IndexOfReference(snapshot.Node.Inputs, input);
            if (currentIndex < 0)
            {
                snapshot.Node.RestoreInput(input, i);
            }
            else
            {
                snapshot.Node.RestoreInputOrder(input, i);
            }

            snapshot.Node.RestoreInputStateForRollback(input, i, snapshot.States[i]);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioContext));
    }

    private void ThrowIfTopologyMutation()
    {
        if (_topologyMutationInProgress)
        {
            throw new InvalidOperationException(
                "Audio graph mutations are not allowed while a topology transaction is in progress.");
        }
    }
}
