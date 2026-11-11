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
    private readonly HashSet<AudioNode> _outputNodes = new(ReferenceEqualityComparer.Instance);
    private List<AudioNode>? _previousNodes;
    private AudioNode? _currentNode;
    private bool _disposed;

    public int SampleRate { get; }

    public AudioChannelLayout ChannelLayout { get; }

    public int ChannelCount => ChannelLayout.ChannelCount;

    // The sample rate of the audio at the end of the chain being composed. A sound whose source runs at another rate
    // sets it before its effects, and a node that converts to SampleRate sets it back, so that the next stage knows
    // what it is given.
    internal int ChainSampleRate
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    }

    public AudioContext(int sampleRate, int channelCount)
        : this(sampleRate, AudioChannelLayout.GetDefault(channelCount))
    {
    }

    public AudioContext(int sampleRate, AudioChannelLayout layout)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(layout.ChannelCount, nameof(layout));

        SampleRate = sampleRate;
        ChannelLayout = layout;
        ChainSampleRate = sampleRate;
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

        if (!ContainsReference(_nodes, node))
        {
            _nodes.Add(node);
        }

        node.SeedChannelCount(ChannelCount);
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
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));
        ArgumentNullException.ThrowIfNull(updater, nameof(updater));
        ArgumentNullException.ThrowIfNull(comparer, nameof(comparer));

        if (_previousNodes?.OfType<TNode>().FirstOrDefault(n => comparer(parameters, n)) is { } existing)
        {
            updater(parameters, Reclaim(existing));
            return AddNode(existing);
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
        ArgumentNullException.ThrowIfNull(source);
        return CreateSourceNode(source, AudioChannelMap.Auto);
    }

    public SourceNode CreateSourceNode(SoundSource.Resource source, AudioChannelMap channelMap)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channelMap);
        AudioChannelLayout clipLayout = channelMap.ResolveClipLayout(source.ChannelLayout);

        if (_previousNodes?.OfType<SourceNode>().FirstOrDefault(n => source.Compare(n.Source)) is { } existing)
        {
            if (existing.ClipLayout != clipLayout || !existing.ChannelMap.Equals(channelMap))
                RetireDependentNodes(existing);
            existing.ClipLayout = clipLayout;
            existing.ChannelMap = channelMap;
            return AddNode(Reclaim(existing));
        }

        var node = new SourceNode { Source = source.Capture(), ClipLayout = clipLayout, ChannelMap = channelMap };
        return AddNode(node);
    }

    public PannerNode CreatePannerNode(IProperty<float> pan, IProperty<float> surroundPan,
        IProperty<float> centerLevel, IProperty<float> lfeLevel, AudioChannelLayout inputLayout)
    {
        ArgumentNullException.ThrowIfNull(pan);
        ArgumentNullException.ThrowIfNull(surroundPan);
        ArgumentNullException.ThrowIfNull(centerLevel);
        ArgumentNullException.ThrowIfNull(lfeLevel);
        var node = CreateNode(pan,
            p => new PannerNode { Pan = p, SurroundPan = surroundPan, CenterLevel = centerLevel, LfeLevel = lfeLevel },
            static (_, _) => { },
            static (p, existing) => ReferenceEquals(p, existing.Pan));
        node.InputLayout = inputLayout;
        node.OutputLayout = ChannelLayout;
        return node;
    }

    /// <summary>
    /// Creates and adds a gain node to the context.
    /// </summary>
    /// <param name="gain">The gain value.</param>
    /// <returns>The created gain node.</returns>
    public GainNode CreateGainNode(IProperty<float> gain)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(gain);

        if (_previousNodes?.OfType<GainNode>().FirstOrDefault(n => ReferenceEquals(n.Gain, gain)) is { } existing)
        {
            // Matched by Gain reference, so no re-assignment is needed (Gain is init-only).
            return AddNode(Reclaim(existing));
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
    public ShiftNode CreateShiftNode(MediaTime shift)
    {
        ThrowIfDisposed();

        if (_previousNodes?.OfType<ShiftNode>().FirstOrDefault(n => n.Offset is null && n.Shift == shift) is { } existing)
        {
            return AddNode(Reclaim(existing));
        }

        var node = new ShiftNode
        {
            Shift = shift
        };
        return AddNode(node);
    }

    // A shift node for a sound's offset. A fixed offset makes a node per value, as editing it composes the sound
    // again; one under an expression is read for each window so that the expression takes effect.
    internal ShiftNode CreateShiftNode(IExpressionProperty<MediaTime> offset)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(offset);

        if (!offset.HasExpression)
            return CreateShiftNode(offset.Value);

        if (_previousNodes?.OfType<ShiftNode>().FirstOrDefault(n => ReferenceEquals(n.Offset, offset)) is { } existing)
        {
            return AddNode(Reclaim(existing));
        }

        return AddNode(new ShiftNode { Offset = offset });
    }

    /// <summary>
    /// Creates and adds a clip node to the context.
    /// </summary>
    /// <param name="start">The start time of the clip.</param>
    /// <param name="duration">The duration of the clip.</param>
    public ClipNode CreateClipNode(MediaTime start, MediaTime duration)
    {
        ThrowIfDisposed();
        if (duration <= MediaTime.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");

        if (_previousNodes?.OfType<ClipNode>().FirstOrDefault(n => n.Duration == duration && n.Start == start) is { } existing)
        {
            return AddNode(Reclaim(existing));
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

        if (_previousNodes?.OfType<MixerNode>().FirstOrDefault() is { } existing)
        {
            return AddNode(Reclaim(existing));
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
        if (sourceSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSampleRate), "Source sample rate must be positive.");

        if (_previousNodes?.OfType<ResampleNode>().FirstOrDefault(n => n.SourceSampleRate == sourceSampleRate) is { } existing)
        {
            return AddNode(Reclaim(existing));
        }

        var node = new ResampleNode { SourceSampleRate = sourceSampleRate };
        return AddNode(node);
    }

    /// <summary>
    /// Creates and adds a speed node to the context.
    /// </summary>
    /// <param name="speed">The playback speed multiplier (100.0 = normal speed).</param>
    /// <returns>The created speed node.</returns>
    public SpeedNode CreateSpeedNode(IProperty<float> speed) => CreateSpeedNode(speed, ChannelCount);

    public SpeedNode CreateSpeedNode(IProperty<float> speed, int channelCount)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(speed);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        if (_previousNodes?.OfType<SpeedNode>().FirstOrDefault(n => ReferenceEquals(n.Speed, speed)) is { } existing)
        {
            existing.ChannelCount = channelCount;
            return AddNode(Reclaim(existing));
        }

        var node = new SpeedNode
        {
            Speed = speed,
            ChannelCount = channelCount
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

        if (ReferenceEquals(source, destination))
            throw new ArgumentException("Cannot connect a node to itself.");

        if (!ContainsReference(_nodes, source))
            AddNode(source);
        if (!ContainsReference(_nodes, destination))
            AddNode(destination);

        destination.AddInput(source);
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

        foreach (var node in _nodes)
        {
            node.Dispose();
        }

        _nodes.Clear();
        _outputNodes.Clear();
        _currentNode = null;
    }

    /// <summary>
    /// Clears all connections while keeping nodes.
    /// </summary>
    public void ClearConnections()
    {
        ThrowIfDisposed();

        foreach (AudioNode node in _nodes)
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

        if (!RemoveReference(_nodes, node))
            return;

        foreach (AudioNode otherNode in _nodes)
        {
            otherNode.RemoveInput(node);
        }

        _outputNodes.Remove(node);
        if (ReferenceEquals(_currentNode, node))
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
    /// Begins a differential update session.
    /// </summary>
    public void BeginUpdate(IEnumerable<AudioNode> previousNodes)
    {
        ThrowIfDisposed();

        _previousNodes = previousNodes.ToList();

        _nodes.Clear();
        _outputNodes.Clear();
        _currentNode = null;
    }

    /// <summary>
    /// Completes a differential update session.
    /// </summary>
    public void EndUpdate()
    {
        ThrowIfDisposed();

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

    // Takes a node matched in the previous graph out of the reuse pool and detaches its old inputs.
    private TNode Reclaim<TNode>(TNode node)
        where TNode : AudioNode
    {
        RemoveReference(_previousNodes!, node);
        node.ClearInputs();
        return node;
    }

    // Routing changes invalidate buffered samples, even when the channel count stays the same.
    private void RetireDependentNodes(AudioNode source)
    {
        HashSet<AudioNode> affected = new(ReferenceEqualityComparer.Instance) { source };
        bool added;
        do
        {
            added = false;
            foreach (var node in _previousNodes!)
            {
                if (!affected.Contains(node) && node.Inputs.Any(affected.Contains))
                    added |= affected.Add(node);
            }
        } while (added);

        foreach (var node in affected)
        {
            if (!ReferenceEquals(node, source))
            {
                RemoveReference(_previousNodes!, node);
                node.Dispose();
            }
        }
    }

    // A context for a child sound that takes its reused nodes from this context's pool.
    internal AudioContext CreateChildContext()
    {
        var child = new AudioContext(SampleRate, ChannelLayout);
        child._previousNodes = _previousNodes;
        return child;
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioContext));
    }
}
