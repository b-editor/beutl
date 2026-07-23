using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

internal sealed class NodeRecordingTransaction : IRenderFragmentHandleOwner
{
    private readonly IRenderRequestRecordingHost _host;
    private readonly NodeRecordingTransaction? _parent;
    private readonly object _origin;
    private readonly HashSet<RenderFragmentReference> _ownedReferences =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<RecordedRenderFragmentEntry> _fragments = [];
    private readonly List<RenderFragmentReference> _publications = [];
    private readonly List<RenderResource> _resources = [];
    private readonly List<RecordedNestedRenderRequest> _nestedRequests = [];
    private readonly List<BuiltInBackdropBinding> _builtInBackdropBindings = [];
    private bool _cacheDisabled;

    public NodeRecordingTransaction(
        IRenderRequestRecordingHost host,
        object origin,
        IEnumerable<RenderFragmentReference> inputs,
        NodeRecordingTransaction? parent = null)
    {
        _host = host;
        _parent = parent;
        _origin = origin ?? throw new ArgumentNullException(nameof(origin));
        ArgumentNullException.ThrowIfNull(inputs);

        var facades = new List<RenderFragmentHandle>();
        foreach (RenderFragmentReference input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            _ownedReferences.Add(input);
            facades.Add(new RenderFragmentHandle(this, input));
        }

        Inputs = facades;
    }

    public IReadOnlyList<RenderFragmentHandle> Inputs { get; }

    public RenderRequest Request => _host.Request;

    public bool IsRenderCacheEnabled
        => State == NodeRecordingTransactionState.Active
           && !_cacheDisabled
           && (_parent?.IsRenderCacheEnabled ?? _host.IsRenderCacheEnabled);

    public NodeRecordingTransactionState State { get; private set; }

    public RenderFragmentHandle CreateFragment(
        RenderFragmentKind kind,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderValueCardinality valueCardinality,
        bool contributesValuesToTarget,
        bool canBeUsedAsValueInput,
        bool hasTargetEffects,
        bool hasOpaqueExternalWork,
        IEnumerable<RenderFragmentReference>? inputs,
        object? payload,
        Func<Point, bool>? hitTest,
        RenderFragmentBoundsRequirement boundsRequirement = RenderFragmentBoundsRequirement.Finite)
    {
        VerifyActive();
        ImmutableArray<RenderFragmentReference> inputCopy = inputs is null ? [] : [.. inputs];
        foreach (RenderFragmentReference input in inputCopy)
        {
            VerifyOwns(input);
        }

        var reference = new RenderFragmentReference(
            kind,
            bounds,
            effectiveScale,
            valueCardinality,
            contributesValuesToTarget,
            canBeUsedAsValueInput,
            hasTargetEffects,
            hasOpaqueExternalWork,
            inputCopy,
            payload,
            hitTest,
            boundsRequirement);
        _ownedReferences.Add(reference);
        _fragments.Add(new RecordedRenderFragmentEntry(reference, _origin, "RenderNode.Process"));
        return new RenderFragmentHandle(this, reference);
    }

    public RenderFragmentReference GetReference(RenderFragmentHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return handle.GetReference(this);
    }

    public ImmutableArray<RenderFragmentReference> GetReferences(
        IEnumerable<RenderFragmentHandle> handles,
        string parameterName)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(handles, parameterName);
        var result = ImmutableArray.CreateBuilder<RenderFragmentReference>();
        foreach (RenderFragmentHandle handle in handles)
        {
            if (handle is null)
                throw new ArgumentException("A fragment sequence cannot contain null handles.", parameterName);
            result.Add(handle.GetReference(this));
        }

        return result.ToImmutable();
    }

    public void Publish(RenderFragmentHandle handle)
    {
        RenderFragmentReference reference = GetReference(handle);
        _publications.Add(reference);
    }

    public void PassThrough()
    {
        VerifyActive();
        foreach (RenderFragmentHandle input in Inputs)
        {
            _publications.Add(input.GetReference(this));
        }
    }

    public void DisableRenderCache()
    {
        VerifyActive();
        _cacheDisabled = true;
    }

    public RenderResource<T> Own<T>(T resource, object? cacheKey, long version)
        where T : class, IDisposable
    {
        VerifyActive();
        RenderResource<T> token = Request.Options.Owner.ResourceRegistry.RegisterOwned(
            resource,
            cacheKey,
            version);
        _resources.Add(token);
        return token;
    }

    public RenderResource<T> Borrow<T>(T resource, object? cacheKey, long version)
        where T : class
    {
        VerifyActive();
        RenderResource<T> token = Request.Options.Owner.ResourceRegistry.RegisterBorrowed(
            resource,
            cacheKey,
            version);
        _resources.Add(token);
        return token;
    }

    public void RollbackResources(IReadOnlyList<RenderResource> resources)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(resources);

        var transactionIndices = new int[resources.Count];
        var claimed = new HashSet<int>();
        for (int resourceIndex = resources.Count - 1; resourceIndex >= 0; resourceIndex--)
        {
            RenderResource resource = resources[resourceIndex];
            int transactionIndex = -1;
            for (int candidate = _resources.Count - 1; candidate >= 0; candidate--)
            {
                if (!claimed.Contains(candidate) && ReferenceEquals(_resources[candidate], resource))
                {
                    transactionIndex = candidate;
                    break;
                }
            }

            if (transactionIndex < 0 || !claimed.Add(transactionIndex))
            {
                throw new InvalidOperationException(
                    "The render resource does not belong to this recording transaction.");
            }

            transactionIndices[resourceIndex] = transactionIndex;
        }

        List<Exception>? failures = null;
        for (int resourceIndex = resources.Count - 1; resourceIndex >= 0; resourceIndex--)
        {
            RenderResource resource = resources[resourceIndex];
            int transactionIndex = transactionIndices[resourceIndex];
            _resources.RemoveAt(transactionIndex);
            for (int earlier = 0; earlier < resourceIndex; earlier++)
            {
                if (transactionIndices[earlier] > transactionIndex)
                    transactionIndices[earlier]--;
            }

            try
            {
                if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
                    Request.Options.Owner.ResourceRegistry.Rollback(resource);
                else if (resource.RegistrationState == RenderResourceRegistrationState.Committed)
                    Request.Options.Owner.ResourceRegistry.Release(resource);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more render resources failed to roll back.", failures);
    }

    public IReadOnlyList<RenderFragmentHandle> RecordNode(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> inputs,
        bool subtree)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(node);
        ImmutableArray<RenderFragmentReference> inputReferences = GetReferences(inputs, nameof(inputs));
        IReadOnlyList<RenderFragmentReference> outputs =
            _host.RecordNode(this, node, inputReferences, subtree);
        return MapReferences(outputs);
    }

    public RecordedNestedRenderRequest RecordNestedRequest(
        RenderNode root,
        RenderRequestOptions options)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);
        RecordedNestedRenderRequest nested = _host.RecordNestedRequest(root, options);
        _nestedRequests.Add(nested);
        return nested;
    }

    public void BindBuiltInBackdrop(
        object identity,
        RenderFragmentHandle capture)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(identity);
        RenderFragmentReference reference = GetReference(capture);
        if (reference.Kind is not (RenderFragmentKind.TargetCapture or RenderFragmentKind.BuiltInBackdropCapture))
        {
            throw new ArgumentException(
                "A built-in backdrop binding requires a target-capture fragment.",
                nameof(capture));
        }

        _builtInBackdropBindings.RemoveAll(binding => ReferenceEquals(binding.Identity, identity));
        _builtInBackdropBindings.Add(new BuiltInBackdropBinding(identity, reference));
    }

    public bool TryGetBuiltInBackdrop(
        object identity,
        out RenderFragmentHandle? handle)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(identity);
        if (TryGetBuiltInBackdropReference(identity, out RenderFragmentReference? reference))
        {
            handle = MapReference(reference!);
            return true;
        }

        handle = null;
        return false;
    }

    public ImmutableArray<RenderFragmentReference> Commit()
    {
        VerifyActive();
        ImmutableArray<RecordedRenderFragmentEntry> fragments = [.. _fragments];
        ValidatePublicationFanOut(SelectReachableFragments());
        var commit = new NodeRecordingCommit(
            fragments,
            [.. _publications],
            [.. _resources],
            [.. _nestedRequests],
            [.. _builtInBackdropBindings],
            _cacheDisabled);

        try
        {
            if (_parent is null)
                _host.Commit(commit);
            else
                _parent.Absorb(commit);

            State = NodeRecordingTransactionState.Committed;
            return commit.Publications;
        }
        catch (Exception ex)
        {
            Rollback(ex);
            throw;
        }
    }

    public void Rollback(Exception primaryFailure)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        if (State != NodeRecordingTransactionState.Active)
        {
            Request.Options.Owner.RecordPrimaryFailure(primaryFailure);
            Request.Options.Owner.ThrowIfFailed();
            return;
        }

        State = NodeRecordingTransactionState.RolledBack;
        Request.Options.Owner.RecordPrimaryFailure(primaryFailure);
        for (int index = _resources.Count - 1; index >= 0; index--)
        {
            try
            {
                RenderResource resource = _resources[index];
                if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
                    Request.Options.Owner.ResourceRegistry.Rollback(resource);
                else
                    Request.Options.Owner.ResourceRegistry.Release(resource);
            }
            catch (Exception ex)
            {
                Request.Options.Owner.RecordPrimaryFailure(ex);
            }
        }


        for (int index = _nestedRequests.Count - 1; index >= 0; index--)
        {
            try
            {
                _nestedRequests[index].Request.Dispose();
            }
            catch (Exception ex)
            {
                Request.Options.Owner.RecordPrimaryFailure(ex);
            }
        }

        Request.Options.Owner.ThrowIfFailed();
    }

    public void VerifyActive()
    {
        if (State != NodeRecordingTransactionState.Active)
        {
            throw new InvalidOperationException(
                "The render-node recording context and its fragment handles are no longer active.");
        }
    }

    public void VerifyOwns(RenderFragmentReference reference)
    {
        VerifyActive();
        if (!_ownedReferences.Contains(reference))
        {
            throw new InvalidOperationException(
                "The render fragment belongs to a different recording transaction.");
        }
    }

    private IReadOnlyList<RenderFragmentHandle> MapReferences(
        IEnumerable<RenderFragmentReference> references)
    {
        VerifyActive();
        var result = new List<RenderFragmentHandle>();
        foreach (RenderFragmentReference reference in references)
        {
            _ownedReferences.Add(reference);
            result.Add(new RenderFragmentHandle(this, reference));
        }

        return result;
    }

    private void Absorb(NodeRecordingCommit child)
    {
        VerifyActive();
        _fragments.AddRange(child.Fragments);
        _resources.AddRange(child.Resources);
        _nestedRequests.AddRange(child.NestedRequests);
        foreach (BuiltInBackdropBinding binding in child.BuiltInBackdropBindings)
        {
            _builtInBackdropBindings.RemoveAll(item => ReferenceEquals(item.Identity, binding.Identity));
            _builtInBackdropBindings.Add(binding);
        }
        _cacheDisabled |= child.CacheDisabled;
    }

    private ImmutableArray<RecordedRenderFragmentEntry> SelectReachableFragments()
    {
        var reachable = new HashSet<RenderFragmentReference>(
            _publications,
            ReferenceEqualityComparer.Instance);
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            RenderFragmentReference reference = _fragments[index].Reference;
            if (!reachable.Contains(reference))
                continue;

            foreach (RenderFragmentReference input in reference.Inputs)
                reachable.Add(input);
        }

        return [.. _fragments.Where(entry => reachable.Contains(entry.Reference))];
    }

    private void ValidatePublicationFanOut(
        ImmutableArray<RecordedRenderFragmentEntry> fragments)
    {
        var counts = new Dictionary<RenderFragmentReference, int>(ReferenceEqualityComparer.Instance);
        foreach (RecordedRenderFragmentEntry entry in fragments)
        {
            foreach (RenderFragmentReference input in entry.Reference.Inputs)
                CountUse(input, counts);
        }

        foreach (RenderFragmentReference publication in _publications)
            CountUse(publication, counts);
    }

    private static void CountUse(
        RenderFragmentReference reference,
        Dictionary<RenderFragmentReference, int> counts)
    {
        counts.TryGetValue(reference, out int count);
        count++;
        counts[reference] = count;
        if (count > 1 && !reference.AllowsFanOut)
        {
            throw new InvalidOperationException(
                "A target-effect render fragment cannot be consumed or published more than once.");
        }
    }

    private bool TryGetBuiltInBackdropReference(
        object identity,
        out RenderFragmentReference? reference)
    {
        VerifyActive();
        for (int index = _builtInBackdropBindings.Count - 1; index >= 0; index--)
        {
            BuiltInBackdropBinding binding = _builtInBackdropBindings[index];
            if (ReferenceEquals(binding.Identity, identity))
            {
                reference = binding.Reference;
                return true;
            }
        }

        if (_parent is not null)
            return _parent.TryGetBuiltInBackdropReference(identity, out reference);
        return Request.Options.Owner.TryGetBuiltInBackdrop(identity, out reference);
    }

    private RenderFragmentHandle MapReference(RenderFragmentReference reference)
    {
        VerifyActive();
        _ownedReferences.Add(reference);
        return new RenderFragmentHandle(this, reference);
    }
}

internal interface IRenderRequestRecordingHost
{
    RenderRequest Request { get; }

    bool IsRenderCacheEnabled { get; }

    IReadOnlyList<RenderFragmentReference> RecordNode(
        NodeRecordingTransaction parent,
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        bool subtree);

    RecordedNestedRenderRequest RecordNestedRequest(
        RenderNode root,
        RenderRequestOptions options);

    void Commit(NodeRecordingCommit commit);
}

internal sealed record NodeRecordingCommit(
    ImmutableArray<RecordedRenderFragmentEntry> Fragments,
    ImmutableArray<RenderFragmentReference> Publications,
    ImmutableArray<RenderResource> Resources,
    ImmutableArray<RecordedNestedRenderRequest> NestedRequests,
    ImmutableArray<BuiltInBackdropBinding> BuiltInBackdropBindings,
    bool CacheDisabled);

internal sealed record RecordedRenderFragmentEntry(
    RenderFragmentReference Reference,
    object Origin,
    string Role);

internal sealed record BuiltInBackdropBinding(
    object Identity,
    RenderFragmentReference Reference);

internal enum NodeRecordingTransactionState : byte
{
    Active,
    Committed,
    RolledBack,
}
