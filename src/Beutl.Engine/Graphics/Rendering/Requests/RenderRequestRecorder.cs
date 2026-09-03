using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderRequestRecorder : IRenderRequestRecordingHost
{
    private static readonly ConditionalWeakTable<RenderNode, RenderNodeCacheIdentity> s_cacheIdentities = new();
    private readonly RecordedRenderGraphBuilder _builder;
    private readonly List<PendingRenderCacheCandidate> _pendingCacheCandidates = [];
    private readonly HashSet<RenderNode> _cacheCandidateNodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<RenderNode> _preparedNodes = new(ReferenceEqualityComparer.Instance);
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
            IReadOnlyList<RenderFragmentReference> outputs = RecordSubtreeCore(root, parent: null);
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

        // These fragments land in the caller's commit, so they are part of the caller's recording and the
        // caller cannot be replayed independently of them. Whether this node is served is its own question,
        // and its input digests answer it however the node was reached.
        ImmutableArray<RenderFragmentReference> outputs = subtree
            ? RecordSubtreeCore(node, parent)
            : InvokeNode(node, inputs, parent, guardAlreadyHeld: false);
        parent.MarkAbsorbedRecording();
        return outputs;
    }

    public void Commit(in NodeRecordingCommit commit)
    {
        _builder.Append(in commit);
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

    private ImmutableArray<RenderFragmentReference> RecordSubtreeCore(
        RenderNode node,
        NodeRecordingTransaction? parent)
    {
        using ActiveNodeScope scope = EnterNode(node);
        PrepareForRequest(node);
        var inputs = new List<RenderFragmentReference>();
        if (node is ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
                inputs.AddRange(RecordSubtreeCore(child, parent));
        }

        return InvokeNode(node, inputs, parent, guardAlreadyHeld: true);
    }

    private ImmutableArray<RenderFragmentReference> InvokeNode(
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        NodeRecordingTransaction? parent,
        bool guardAlreadyHeld)
    {
        ActiveNodeScope scope = default;
        if (!guardAlreadyHeld)
        {
            scope = EnterNode(node);

            // The subtree walk prepares a node on its way down. A node reached with explicit inputs is not
            // walked, so this is where it is prepared instead - the contract is that PrepareForRequest runs
            // before Process, on every request, however the node is reached.
            PrepareForRequest(node);
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
                // the ones it was recorded over. A descendant that re-recorded is not itself a reason to
                // re-record here - what it produced is, and the digest is what reports that.
                bool repeats = !node.HasChanges
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

                bool canCache = transaction.IsRenderCacheEnabled
                                && node.Cache.CanCapture
                                && !node.HasChanges
                                && !node.Cache.IsDisposed;
                ImmutableArray<RenderFragmentReference> outputs = transaction.Commit();
                if (canCache)
                    QueueCacheCandidates(node, outputs);
                if (_crossCheckProbeDepth == 0)
                    RetainRecording(key, node, inputs, transaction, snapshot, repeats);
                return outputs;
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

    /// <summary>Gives <paramref name="node"/> its one preparation for this request.</summary>
    /// <remarks>
    /// A node reachable from more than one parent is recorded once per parent, and each of those recordings
    /// arrives here. Preparing it again would let an override that rebuilds or replaces request-dependent
    /// children invalidate what the earlier occurrence already recorded over them.
    /// </remarks>
    private void PrepareForRequest(RenderNode node)
    {
        if (_preparedNodes.Add(node))
            node.PrepareForRequest(new RenderNodePreparation(Request.Options));
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
