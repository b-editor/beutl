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

        return subtree
            ? RecordSubtreeCore(node, parent)
            : InvokeNode(node, inputs, parent, guardAlreadyHeld: false);
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
        using RenderRecordingFamily.Scope scope = EnterNode(node);
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
        RenderRecordingFamily.Scope scope = default;
        try
        {
            if (!guardAlreadyHeld)
            {
                scope = EnterNode(node);

                // The subtree walk prepares a node on its way down. A node reached with explicit inputs is not
                // walked, so this is where it is prepared instead - the contract is that PrepareForRequest runs
                // before Process, on every request, however the node is reached.
                PrepareForRequest(node);
            }

            var transaction = new NodeRecordingTransaction(this, node, inputs, parent);
            try
            {
                node.Process(new RenderNodeContext(transaction));
                return CommitNodeRecording(node, transaction);
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

    // The cache flags are read before Commit: IsRenderCacheEnabled reports false once the transaction leaves Active.
    private ImmutableArray<RenderFragmentReference> CommitNodeRecording(
        RenderNode node,
        NodeRecordingTransaction transaction)
    {
        bool canCache = transaction.IsRenderCacheEnabled
                        && node.Cache.CanCapture
                        && !node.HasChanges
                        && !node.Cache.IsDisposed;
        bool cacheDisabled = Request.Options.CachePolicy.IsEnabled && !transaction.IsRenderCacheEnabled;
        ImmutableArray<RenderFragmentReference> outputs = transaction.Commit();
        if (cacheDisabled)
            Request.DisableCacheForOutputs(outputs);
        if (canCache)
            QueueCacheCandidates(node, outputs);
        return outputs;
    }

    /// <summary>Gives <paramref name="node"/> its one preparation for this request.</summary>
    /// <remarks>
    /// A node reachable from more than one parent is recorded once per parent, and each of those recordings
    /// arrives here. Preparing it again would let an override that rebuilds or replaces request-dependent
    /// children invalidate what the earlier occurrence already recorded over them.
    /// </remarks>
    private void PrepareForRequest(RenderNode node)
    {
        if (!_preparedNodes.Add(node))
            return;

        try
        {
            node.PrepareForRequest(new RenderNodePreparation(Request.Options));
        }
        catch
        {
            _preparedNodes.Remove(node);
            throw;
        }
    }

    private RenderRecordingFamily.Scope EnterNode(RenderNode node)
        => Request.Options.Owner.RecordingFamily.Enter(node);

    private void QueueCacheCandidates(
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> outputs)
    {
        // RenderNodeCache owns one atomic output set. Multiple independently published fragments would
        // require a compound candidate identity and are conservatively left uncached for now.
        if (outputs.Count != 1)
            return;

        // A node reachable from several parents offers its cache once; later recordings render uncached, which
        // costs work, not correctness.
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

    private sealed class RenderNodeCacheIdentity
    {
    }

    private sealed record PendingRenderCacheCandidate(
        RenderFragmentReference Reference,
        RenderNodeCacheIdentity Identity,
        RenderNodeCache Cache);
}
