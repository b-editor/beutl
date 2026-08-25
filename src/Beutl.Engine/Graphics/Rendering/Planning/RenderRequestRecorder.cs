using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderRequestRecorder : IRenderRequestRecordingHost
{
    private static readonly ConditionalWeakTable<RenderNode, RenderNodeCacheIdentity> s_cacheIdentities = new();
    private readonly RecordedRenderGraphBuilder _builder;
    private readonly List<PendingRenderCacheCandidate> _pendingCacheCandidates = [];
    private readonly HashSet<RenderNode> _cacheCandidateNodes = new(ReferenceEqualityComparer.Instance);
    private int _crossCheckProbeDepth;
    private bool _recorded;

    public RenderRequestRecorder(RenderRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _builder = new RecordedRenderGraphBuilder(request.Id);
        IsRenderCacheEnabled = request.Options.CachePolicy.IsEnabled;
    }

    public RenderRequest Request { get; }

    // The request-wide cache policy, not a running tally. RecordSubtreeCore hands every node of a container
    // hierarchy the same parent, so a node that latched this on commit would decide for every node recorded
    // after it in the request - including unrelated siblings.
    public bool IsRenderCacheEnabled { get; }

    public RecordedRenderGraph Record(RenderNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_recorded)
            throw new InvalidOperationException("A render request recorder can record its root only once.");
        if (Request.State != RenderRequestState.Created)
            throw new InvalidOperationException("A render request must be newly created before recording.");

        _recorded = true;
        Request.TransitionTo(RenderRequestState.Recording);
        try
        {
            IReadOnlyList<RenderFragmentReference> outputs = RecordSubtreeCore(root, parent: null).Outputs;
            CommitCacheCandidates();
            foreach (RenderFragmentReference output in outputs)
            {
                RenderFragmentId id = output.Id
                    ?? throw new InvalidOperationException("A root publication was not committed to the request graph.");
                _builder.PublishRoot(id);
            }

            Request.TransitionTo(RenderRequestState.Recorded);
            return _builder.Build();
        }
        catch (Exception ex)
        {
            if (Request.State is not (RenderRequestState.Failed or RenderRequestState.Disposed))
                Request.Fail(ex);
            Request.Options.Owner.ThrowIfFailed();
            throw;
        }
    }

    public IReadOnlyList<RenderFragmentReference> RecordNode(
        NodeRecordingTransaction parent,
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        bool subtree)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(inputs);

        // These fragments land in the caller's commit, so the caller's recording repeats only if this one
        // does. A node reached with explicit inputs is not walked, and nothing here can tell whether those
        // inputs repeat what it was recorded over, so only an input-free one is offered its own cache.
        NodeRecording recording = subtree
            ? RecordSubtreeCore(node, parent)
            : InvokeNode(node, inputs, inputs.Count == 0, parent, guardAlreadyHeld: false);
        parent.MarkAbsorbedRecording(recording.RepeatsPreviousRecording);
        return recording.Outputs;
    }

    public void Commit(NodeRecordingCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        _builder.Append(commit);
        foreach (RenderResource resource in commit.Resources)
        {
            Request.Options.Owner.ResourceRegistry.Commit(resource);
        }

        Request.Options.Owner.CommitBuiltInBackdropBindings(commit.BuiltInBackdropBindings);
    }

    public RecordedNestedRenderRequest RecordNestedRequest(
        RenderNode root,
        RenderRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        var nestedRequest = new RenderRequest(options, Request);
        try
        {
            var recorder = new RenderRequestRecorder(nestedRequest);
            RecordedRenderGraph graph = recorder.Record(root);
            return new RecordedNestedRenderRequest(nestedRequest, graph);
        }
        catch
        {
            nestedRequest.Dispose();
            throw;
        }
    }

    private NodeRecording RecordSubtreeCore(
        RenderNode node,
        NodeRecordingTransaction? parent)
    {
        using ActiveNodeScope scope = EnterNode(node);
        node.PrepareForRequest(new RenderNodePreparation(Request.Options));
        var inputs = new List<RenderFragmentReference>();
        bool inputsRepeat = true;
        if (node is ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
            {
                NodeRecording childRecording = RecordSubtreeCore(child, parent);
                inputs.AddRange(childRecording.Outputs);
                inputsRepeat &= childRecording.RepeatsPreviousRecording;
            }
        }

        return InvokeNode(node, inputs, inputsRepeat, parent, guardAlreadyHeld: true);
    }

    private NodeRecording InvokeNode(
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        bool inputsRepeatPreviousRecording,
        NodeRecordingTransaction? parent,
        bool guardAlreadyHeld)
    {
        ActiveNodeScope scope = default;
        if (!guardAlreadyHeld)
        {
            scope = EnterNode(node);

            // The subtree walk already prepared this node on its way down. A node reached with explicit
            // inputs is not walked, so this is where it gets its one call for the request - the contract is
            // that PrepareForRequest runs before Process, on every request, however the node is reached.
            node.PrepareForRequest(new RenderNodePreparation(Request.Options));
        }

        try
        {
            var transaction = new NodeRecordingTransaction(this, node, inputs, parent);
            try
            {
                RenderNodeRecordingKey key = RenderNodeRecordingKey.Create(
                    Request.Options,
                    transaction.IsRenderCacheEnabled);
                RenderNodeRecordingSnapshot? snapshot =
                    _crossCheckProbeDepth > 0 ? null : node.RecordingSnapshot;

                // What a skip path needs and nothing more: the node reports no change, the request agrees
                // with the one the recording was made for, and the fragments it is replayed over digest to
                // the ones it was recorded over. Whether the recording is actually reused is a separate
                // question - it repeats either way, which is what the node above needs to know.
                bool repeats = !node.HasChanges
                               && inputsRepeatPreviousRecording
                               && snapshot is not null
                               && snapshot.Matches(key, inputs);

                if (repeats && snapshot!.IsReplayable && !RenderRecordingCrossCheck.IsEnabled)
                {
                    transaction.ReplayRecording(snapshot, inputs);
                }
                else
                {
                    var context = new RenderNodeContext(transaction);
#if DEBUG
                    RecordedNodeShape? crossCheckBaseline = RenderRecordingCrossCheck.CaptureBaseline(
                        this,
                        node,
                        inputs,
                        repeats ? snapshot : null);
#endif
                    node.Process(context);
#if DEBUG
                    RenderRecordingCrossCheck.Verify(node, crossCheckBaseline, inputs, transaction);
#endif
                }

                repeats &= transaction.AbsorbedRecordingsRepeat;
                bool canCache = transaction.IsRenderCacheEnabled
                                && node.Cache.CanCapture
                                && !node.HasChanges
                                && !node.Cache.IsDisposed;
                ImmutableArray<RenderFragmentReference> outputs = transaction.Commit();
                if (canCache)
                    QueueCacheCandidates(node, outputs);
                if (_crossCheckProbeDepth == 0)
                    RetainRecording(key, node, inputs, transaction, snapshot, repeats);
                return new NodeRecording(outputs, repeats);
            }
            catch (Exception ex)
            {
                if (transaction.State == NodeRecordingTransactionState.Active)
                    transaction.Rollback(ex);
                throw;
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    private void RetainRecording(
        in RenderNodeRecordingKey key,
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        NodeRecordingTransaction transaction,
        RenderNodeRecordingSnapshot? previous,
        bool repeats)
    {
        // A repeat re-recorded under the cross-check produces the very fragments the retained snapshot
        // already holds, so keeping it costs nothing and describing it again would allocate on every frame.
        RenderNodeRecordingSnapshot snapshot = repeats && previous is not null
            ? previous
            : RenderNodeRecordingCache.Capture(key, node, inputs, transaction);

        if (RenderRecordingCrossCheck.IsEnabled)
            snapshot.Shape = RecordedNodeShape.Capture(inputs, transaction);

        node.RecordingSnapshot = snapshot;
    }

    private ActiveNodeScope EnterNode(RenderNode node)
    {
        return new ActiveNodeScope(Request.Options.Owner.RecordingFamily.Enter(node));
    }

    /// <summary>Whether this recorder is inside a cross-check probe recording.</summary>
    internal bool IsCapturingCrossCheckBaseline => _crossCheckProbeDepth > 0;

    /// <summary>
    /// Records <paramref name="node"/> into a transaction that never reaches the graph, and describes what it
    /// produced.
    /// </summary>
    /// <remarks>
    /// The probe transaction has no parent, so its own commits and those of anything it records below it stay
    /// inside it and are released together. <see cref="RenderNode.PrepareForRequest"/> is deliberately not
    /// called again: it runs once per request, and the contract under test is what a second
    /// <see cref="RenderNode.Process(RenderNodeContext)"/> alone produces.
    /// </remarks>
    internal RecordedNodeShape CaptureCrossCheckBaseline(
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        _crossCheckProbeDepth++;
        var transaction = new NodeRecordingTransaction(this, node, inputs, parent: null);
        try
        {
            node.Process(new RenderNodeContext(transaction));
            return RecordedNodeShape.Capture(inputs, transaction);
        }
        finally
        {
            transaction.Abandon();
            _crossCheckProbeDepth--;
        }
    }

    private void QueueCacheCandidates(
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> outputs)
    {
        // A probe recording is discarded, so the candidate it would offer names a fragment the graph never
        // received - and claiming the node here would deny the real recording its one candidate.
        if (_crossCheckProbeDepth > 0)
            return;

        // RenderNodeCache owns one atomic output set. Multiple independently published fragments would
        // require a compound candidate identity and are conservatively left uncached for now.
        if (outputs.Count != 1)
            return;

        // A node reachable from more than one parent is recorded once per parent, and each recording would
        // offer the same RenderNodeCache a candidate of its own. Those outputs are only interchangeable when
        // both parents demanded the same thing, so the family would sooner or later try to publish two
        // independent outputs to one cache and fail the frame. The first recording keeps the cache; a later
        // one renders uncached, which costs work rather than correctness.
        if (!_cacheCandidateNodes.Add(node))
            return;

        RenderNodeCacheIdentity identity = s_cacheIdentities.GetValue(
            node,
            static _ => new RenderNodeCacheIdentity());
        foreach (RenderFragmentReference output in outputs)
        {
            if (output.CanBeUsedAsValueInput && output.ValueCardinality.Maximum != 0)
            {
                _pendingCacheCandidates.Add(new PendingRenderCacheCandidate(
                    output,
                    identity,
                    node.Cache));
            }
        }
    }

    private void CommitCacheCandidates()
    {
        foreach (PendingRenderCacheCandidate candidate in _pendingCacheCandidates)
        {
            RenderFragmentId fragmentId = candidate.Reference.Id
                ?? throw new InvalidOperationException(
                    "A cache candidate producer was not committed to the recorded graph.");
            _builder.AddCacheCandidate(fragmentId, candidate.Identity, candidate.Cache);
        }
        _pendingCacheCandidates.Clear();
    }

    /// <summary>What one node recorded, and whether it is what the node recorded for the previous request.</summary>
    private readonly struct NodeRecording(
        ImmutableArray<RenderFragmentReference> outputs,
        bool repeatsPreviousRecording)
    {
        public ImmutableArray<RenderFragmentReference> Outputs { get; } = outputs;

        public bool RepeatsPreviousRecording { get; } = repeatsPreviousRecording;
    }

    private readonly struct ActiveNodeScope : IDisposable
    {
        private readonly IDisposable? _scope;

        public ActiveNodeScope(IDisposable scope)
        {
            _scope = scope;
        }

        public void Dispose() => _scope?.Dispose();
    }

    private sealed class RenderNodeCacheIdentity
    {
    }

    private sealed record PendingRenderCacheCandidate(
        RenderFragmentReference Reference,
        RenderNodeCacheIdentity Identity,
        RenderNodeCache Cache);
}

internal sealed class RenderRecordingFamily
{
    private readonly List<RenderNode> _activeNodes = [];

    public IDisposable Enter(RenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        // The stack never holds a node twice - this method is what keeps it so - hence the scan may run
        // from the top, where a recording cycle closes.
        int cycleStart = -1;
        for (int index = _activeNodes.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_activeNodes[index], node))
            {
                cycleStart = index;
                break;
            }
        }

        if (cycleStart >= 0)
        {
            IEnumerable<string> cycle = _activeNodes
                .Skip(cycleStart)
                .Append(node)
                .Select(static item => item.GetType().FullName ?? item.GetType().Name);
            throw new InvalidOperationException(
                $"A render-node recording cycle was detected: {string.Join(" -> ", cycle)}.");
        }

        _activeNodes.Add(node);
        return new Scope(this, node);
    }

    private void Exit(RenderNode node)
    {
        int index = _activeNodes.Count - 1;
        if (index < 0 || !ReferenceEquals(_activeNodes[index], node))
            throw new InvalidOperationException("The active render-node recording stack is corrupted.");

        _activeNodes.RemoveAt(index);
    }

    private sealed class Scope(RenderRecordingFamily owner, RenderNode node) : IDisposable
    {
        private RenderRecordingFamily? _owner = owner;

        public void Dispose()
        {
            RenderRecordingFamily? current = Interlocked.Exchange(ref _owner, null);
            current?.Exit(node);
        }
    }
}

internal sealed record RecordedNestedRenderRequest(
    RenderRequest Request,
    RecordedRenderGraph Graph);
