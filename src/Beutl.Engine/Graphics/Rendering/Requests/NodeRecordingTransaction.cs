using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class NodeRecordingTransaction : IRenderFragmentHandleOwner, IRenderResourceRecordingScope
{
    private const int RecycledSetSizeLimit = 1024;
    private const int OwnedReferencePoolLimit = 32;

    private readonly IRenderRequestRecordingHost _host;
    private readonly NodeRecordingTransaction? _parent;
    private readonly object _origin;
    private readonly List<RecordedRenderFragmentEntry> _fragments = [];
    private readonly List<RenderFragmentReference> _publications = [];

    // Null until used: most nodes register nothing here, and an empty List<T> still costs an object per visit.
    private List<RenderResource>? _resources;
    private List<RecordedNestedRenderRequest>? _nestedRequests;
    private List<BuiltInBackdropBinding>? _builtInBackdropBindings;
    private List<RecordedHitTestRead>? _hitTestReads;

    // Nulled the moment the transaction seals so a recycled set can never answer for a dead transaction.
    private HashSet<RenderFragmentReference>? _ownedReferences = RentOwnedReferences();
    private HashSet<RenderFragmentReference>? _dropped;
    private bool _cacheDisabled;
    private bool _hasOwnTargetEffectFragment;

    [ThreadStatic]
    private static InvariantScratch? t_scratch;

    [ThreadStatic]
    private static Stack<HashSet<RenderFragmentReference>>? t_ownedReferencePool;

    /// <summary>Where <see cref="ReplayRecording"/> takes its slot scratch from.</summary>
    /// <remarks>
    /// Assignable only so a test can observe the rent/return discipline, including under a mid-replay throw.
    /// The render path never replaces it.
    /// </remarks>
    internal static ArrayPool<RenderFragmentReference> ReplayScratchPool { get; set; } =
        ArrayPool<RenderFragmentReference>.Shared;

    public NodeRecordingTransaction(
        IRenderRequestRecordingHost host,
        object origin,
        IReadOnlyList<RenderFragmentReference> inputs,
        NodeRecordingTransaction? parent = null)
    {
        _host = host;
        _parent = parent;
        _origin = origin ?? throw new ArgumentNullException(nameof(origin));
        ArgumentNullException.ThrowIfNull(inputs);

        int count = inputs.Count;
        RenderFragmentHandle[] facades = count == 0 ? [] : new RenderFragmentHandle[count];
        HashSet<RenderFragmentReference> owned = OwnedReferences;
        for (int index = 0; index < count; index++)
        {
            RenderFragmentReference input = inputs[index];
            ArgumentNullException.ThrowIfNull(input);
            owned.Add(input);
            facades[index] = new RenderFragmentHandle(this, input);
        }

        InputHandles = facades;
    }

    public IReadOnlyList<RenderFragmentHandle> Inputs => InputHandles;

    // Indexing this instead of Inputs keeps the per-visit input walks from boxing an enumerator.
    internal RenderFragmentHandle[] InputHandles { get; }

    public RenderRequest Request => _host.Request;

    public int PublicationCount
    {
        get
        {
            VerifyActive();
            return _publications.Count;
        }
    }

    // Disablement reaches the nodes recorded inside this checkpoint and nobody else. A committed child does
    // not mark its parent, so which sibling ran first cannot change whether the others may be cached.
    public bool IsRenderCacheEnabled
        => State == NodeRecordingTransactionState.Active
           && !_cacheDisabled
           && (_parent?.IsRenderCacheEnabled ?? _host.IsRenderCacheEnabled);

    public NodeRecordingTransactionState State { get; private set; }

    public bool IsRecording => State == NodeRecordingTransactionState.Active;

    private HashSet<RenderFragmentReference> OwnedReferences
        => _ownedReferences ?? throw new InvalidOperationException(
            "The render-node recording context and its fragment handles are no longer active.");

    public RenderFragmentHandle CreateFragment(
        RenderFragmentKind kind,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderValueCardinality valueCardinality,
        bool contributesValuesToTarget,
        bool canBeUsedAsValueInput,
        bool hasTargetEffects,
        bool hasOpaqueExternalWork,
        ImmutableArray<RenderFragmentReference> inputs,
        object? payload,
        RenderFragmentHitTest hitTest,
        RenderFragmentBoundsRequirement boundsRequirement = RenderFragmentBoundsRequirement.Finite,
        bool hasDirectSymbolicBoundsDependency = false)
    {
        VerifyActive();
        ImmutableArray<RenderFragmentReference> inputCopy = inputs.IsDefault ? [] : inputs;
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
            boundsRequirement,
            hasDirectSymbolicBoundsDependency);
        OwnedReferences.Add(reference);
        _fragments.Add(
            new RecordedRenderFragmentEntry(reference, _origin, RenderNodeRecordingCache.ProcessRole));
        _hasOwnTargetEffectFragment |= IsTargetEffect(kind);
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
        if (handles is not IReadOnlyList<RenderFragmentHandle> list)
            return CollectReferences(handles, parameterName);

        int count = list.Count;
        if (count == 0)
            return [];

        // Handed straight to a fragment as its retained Inputs, so this array must be owned by nobody else -
        // a pooled buffer would be read by the recording cache long after its renter returned it.
        var references = new RenderFragmentReference[count];
        for (int index = 0; index < count; index++)
        {
            RenderFragmentHandle handle = list[index];
            if (handle is null)
                throw new ArgumentException("A fragment sequence cannot contain null handles.", parameterName);
            references[index] = handle.GetReference(this);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(references);
    }

    private ImmutableArray<RenderFragmentReference> CollectReferences(
        IEnumerable<RenderFragmentHandle> handles,
        string parameterName)
    {
        ImmutableArray<RenderFragmentReference>.Builder result =
            ImmutableArray.CreateBuilder<RenderFragmentReference>();
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
        PublishCore(GetReference(handle));
    }

    public void PassThrough()
    {
        VerifyActive();
        RenderFragmentHandle[] handles = InputHandles;
        _publications.EnsureCapacity(_publications.Count + handles.Length);
        for (int index = 0; index < handles.Length; index++)
        {
            PublishCore(handles[index].GetReference(this));
        }
    }

    public void Drop(RenderFragmentHandle fragment)
    {
        RenderFragmentReference reference = GetReference(fragment);
        if (_publications.Contains(reference))
        {
            throw new InvalidOperationException(
                "The render fragment was already published and cannot be dropped.");
        }

        (_dropped ??= new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance))
            .Add(reference);
    }

    public void DisableRenderCache()
    {
        VerifyActive();
        _cacheDisabled = true;
    }

    public RenderResource<T> Own<T>(T resource)
        where T : class, IDisposable
    {
        VerifyActive();
        RenderResource<T> token = Request.Options.Owner.ResourceRegistry.RegisterOwned(resource, this);
        (_resources ??= []).Add(token);
        return token;
    }

    public RenderResource<T> Borrow<T>(T resource)
        where T : class
    {
        VerifyActive();
        RenderResource<T> token = Request.Options.Owner.ResourceRegistry.RegisterBorrowed(resource, this);
        (_resources ??= []).Add(token);
        return token;
    }

    public void RollbackResources(IReadOnlyList<RenderResource> resources)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count == 0)
            return;

        List<RenderResource> registered = _resources
            ?? throw new InvalidOperationException(
                "The render resource does not belong to this recording transaction.");

        var transactionIndices = new int[resources.Count];
        var claimed = new HashSet<int>();
        for (int resourceIndex = resources.Count - 1; resourceIndex >= 0; resourceIndex--)
        {
            RenderResource resource = resources[resourceIndex];
            int transactionIndex = -1;
            for (int candidate = registered.Count - 1; candidate >= 0; candidate--)
            {
                if (!claimed.Contains(candidate) && ReferenceEquals(registered[candidate], resource))
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
            registered.RemoveAt(transactionIndex);
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

    public Exception? RollbackResourcesAndCapture(
        IReadOnlyList<RenderResource> resources,
        Exception primaryFailure)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        Request.Options.Owner.RecordPrimaryFailure(primaryFailure);
        try
        {
            RollbackResources(resources);
        }
        catch (AggregateException ex)
        {
            foreach (Exception cleanupFailure in ex.InnerExceptions)
                Request.Options.Owner.RecordCleanupFailure(cleanupFailure);
            return ex;
        }
        catch (Exception ex)
        {
            Request.Options.Owner.RecordCleanupFailure(ex);
            return ex;
        }

        return null;
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
        (_nestedRequests ??= []).Add(nested);
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

        List<BuiltInBackdropBinding> bindings = _builtInBackdropBindings ??= [];
        bindings.RemoveAll(binding => ReferenceEquals(binding.Identity, identity));
        bindings.Add(new BuiltInBackdropBinding(identity, reference));
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
        ValidateRecordedInvariants();
        var commit = new NodeRecordingCommit(
            fragments,
            [.. _publications],
            _resources is null ? [] : [.. _resources],
            _nestedRequests is null ? [] : [.. _nestedRequests],
            _builtInBackdropBindings is null ? [] : [.. _builtInBackdropBindings],
            _dropped is null ? [] : [.. _dropped]);

        try
        {
            if (_parent is null)
                _host.Commit(in commit);
            else
                _parent.Absorb(in commit);

            State = NodeRecordingTransactionState.Committed;
            ReleaseOwnedReferences();
            return commit.Publications;
        }
        catch (Exception ex)
        {
            Rollback(ex);
            throw;
        }
    }

    /// <summary>Releases what an abandoned recording registered, without failing the request.</summary>
    /// <remarks>
    /// <see cref="Rollback"/> is the failure path: it reports a primary failure and the request dies with it.
    /// A cross-check probe recording is discarded on the success path instead, so it needs the cleanup
    /// without the verdict. The fragments it recorded stay readable, having never reached the graph.
    /// </remarks>
    internal void Abandon()
    {
        if (State != NodeRecordingTransactionState.Active)
            return;

        State = NodeRecordingTransactionState.RolledBack;
        ReleaseOwnedReferences();
        List<Exception>? failures = null;
        for (int index = (_resources?.Count ?? 0) - 1; index >= 0; index--)
        {
            try
            {
                RollbackOrRelease(_resources![index]);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        for (int index = (_nestedRequests?.Count ?? 0) - 1; index >= 0; index--)
        {
            try
            {
                _nestedRequests![index].Request.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        _resources?.Clear();
        _nestedRequests?.Clear();
        if (failures is not null)
        {
            throw new AggregateException(
                "An abandoned render-node recording failed to release its resources.",
                failures);
        }
    }

    /// <summary>The fragments this transaction has recorded so far, in creation order.</summary>
    /// <remarks>Read by the recording cross-check; the list is this transaction's own and must not be edited.</remarks>
    internal IReadOnlyList<RecordedRenderFragmentEntry> RecordedFragments => _fragments;

    /// <summary>The fragments this transaction has published so far, in publication order.</summary>
    internal IReadOnlyList<RenderFragmentReference> RecordedPublications => _publications;

    /// <summary>The fragments this transaction abandoned, or <see langword="null"/> when it abandoned none.</summary>
    internal IReadOnlyCollection<RenderFragmentReference>? RecordedDropped => _dropped;

    /// <summary>The hit tests this recording read, in the order it read them.</summary>
    internal IReadOnlyList<RecordedHitTestRead>? RecordedHitTestReads => _hitTestReads;

    internal int RecordedResourceCount => _resources?.Count ?? 0;

    internal int RecordedNestedRequestCount => _nestedRequests?.Count ?? 0;

    internal int RecordedBuiltInBackdropBindingCount => _builtInBackdropBindings?.Count ?? 0;

    /// <summary>Whether this recording called <see cref="DisableRenderCache"/> on itself.</summary>
    internal bool IsRenderCacheDisabledHere => _cacheDisabled;

    /// <summary>How many other nodes this recording drove.</summary>
    internal int AbsorbedRecordingCount { get; private set; }

    internal void MarkAbsorbedRecording() => AbsorbedRecordingCount++;

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
        ReleaseOwnedReferences();
        Request.Options.Owner.RecordPrimaryFailure(primaryFailure);
        for (int index = (_resources?.Count ?? 0) - 1; index >= 0; index--)
        {
            try
            {
                RollbackOrRelease(_resources![index]);
            }
            catch (Exception ex)
            {
                Request.Options.Owner.RecordCleanupFailure(ex);
            }
        }


        for (int index = (_nestedRequests?.Count ?? 0) - 1; index >= 0; index--)
        {
            try
            {
                _nestedRequests![index].Request.Dispose();
            }
            catch (Exception ex)
            {
                Request.Options.Owner.RecordCleanupFailure(ex);
            }
        }

        Request.Options.Owner.ThrowIfFailed();
    }

    // A resource still pending registration is rolled back; one already registered is released.
    private void RollbackOrRelease(RenderResource resource)
    {
        if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
            Request.Options.Owner.ResourceRegistry.Rollback(resource);
        else
            Request.Options.Owner.ResourceRegistry.Release(resource);
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
        if (_ownedReferences?.Contains(reference) != true)
        {
            throw new InvalidOperationException(
                "The render fragment belongs to a different recording transaction.");
        }
    }

    public void NoteHitTestRead(
        RenderFragmentReference reference,
        Point point,
        bool concrete,
        bool result)
        => (_hitTestReads ??= []).Add(new RecordedHitTestRead(reference, point, concrete, result));

    private IReadOnlyList<RenderFragmentHandle> MapReferences(
        IReadOnlyList<RenderFragmentReference> references)
    {
        VerifyActive();
        int count = references.Count;
        if (count == 0)
            return [];

        var result = new RenderFragmentHandle[count];
        HashSet<RenderFragmentReference> owned = OwnedReferences;
        for (int index = 0; index < count; index++)
        {
            RenderFragmentReference reference = references[index];
            owned.Add(reference);
            result[index] = new RenderFragmentHandle(this, reference);
        }

        return result;
    }

    private void Absorb(in NodeRecordingCommit child)
    {
        VerifyActive();
        _fragments.AddRange(child.Fragments.AsSpan());
        if (!child.Resources.IsEmpty)
        {
            foreach (RenderResource resource in child.Resources)
            {
                // The child has sealed, so a registration it never got committed answers to this transaction's
                // rollback from here on - and stays readable for the rest of this recording.
                if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
                    resource.RecordingScope = this;
            }

            (_resources ??= []).AddRange(child.Resources.AsSpan());
        }

        if (!child.NestedRequests.IsEmpty)
            (_nestedRequests ??= []).AddRange(child.NestedRequests.AsSpan());
        if (!child.Dropped.IsEmpty)
        {
            _dropped ??= new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
            foreach (RenderFragmentReference dropped in child.Dropped)
                _dropped.Add(dropped);
        }

        if (!child.BuiltInBackdropBindings.IsEmpty)
        {
            List<BuiltInBackdropBinding> bindings = _builtInBackdropBindings ??= [];
            foreach (BuiltInBackdropBinding binding in child.BuiltInBackdropBindings)
            {
                bindings.RemoveAll(item => ReferenceEquals(item.Identity, binding.Identity));
                bindings.Add(binding);
            }
        }

        // Nothing in the commit type keeps an absorbed entry's origin distinct from this transaction's, so
        // the orphan-check flag is recomputed from the entries rather than assumed off.
        if (!_hasOwnTargetEffectFragment)
        {
            foreach (RecordedRenderFragmentEntry entry in child.Fragments)
            {
                if (ReferenceEquals(entry.Origin, _origin) && IsTargetEffect(entry.Reference.Kind))
                {
                    _hasOwnTargetEffectFragment = true;
                    break;
                }
            }
        }
    }

    // Transactions nest but never overlap their ownership sets: a child rents at construction and returns at
    // its own commit, all inside the parent's recording.
    private static HashSet<RenderFragmentReference> RentOwnedReferences()
    {
        Stack<HashSet<RenderFragmentReference>>? pool = t_ownedReferencePool;
        return pool is not null && pool.TryPop(out HashSet<RenderFragmentReference>? owned)
            ? owned
            : new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
    }

    private void ReleaseOwnedReferences()
    {
        HashSet<RenderFragmentReference>? owned = _ownedReferences;
        _ownedReferences = null;
        if (owned is null || owned.Count > RecycledSetSizeLimit)
            return;

        owned.Clear();
        Stack<HashSet<RenderFragmentReference>> pool =
            t_ownedReferencePool ??= new Stack<HashSet<RenderFragmentReference>>();
        if (pool.Count < OwnedReferencePoolLimit)
            pool.Push(owned);
    }

    private void PublishCore(RenderFragmentReference reference)
    {
        if (_dropped?.Contains(reference) == true)
        {
            throw new InvalidOperationException(
                "The render fragment was already dropped and cannot be published.");
        }

        _publications.Add(reference);
    }

    private bool TryGetBuiltInBackdropReference(
        object identity,
        out RenderFragmentReference? reference)
    {
        VerifyActive();
        for (int index = (_builtInBackdropBindings?.Count ?? 0) - 1; index >= 0; index--)
        {
            BuiltInBackdropBinding binding = _builtInBackdropBindings![index];
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
        OwnedReferences.Add(reference);
        return new RenderFragmentHandle(this, reference);
    }
}

/// <summary>One hit test a recording read, and what it answered.</summary>
internal readonly record struct RecordedHitTestRead(
    RenderFragmentReference Reference,
    Point Point,
    bool Concrete,
    bool Result);
