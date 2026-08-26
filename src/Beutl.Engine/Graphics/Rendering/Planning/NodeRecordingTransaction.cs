using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Beutl.Graphics.Rendering;

internal sealed class NodeRecordingTransaction : IRenderFragmentHandleOwner, IRenderResourceRecordingScope
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
        _fragments.Add(new RecordedRenderFragmentEntry(reference, _origin, "RenderNode.Process"));
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
                RenderResource resource = _resources![index];
                if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
                    Request.Options.Owner.ResourceRegistry.Rollback(resource);
                else
                    Request.Options.Owner.ResourceRegistry.Release(resource);
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

    /// <summary>Records the fragments of <paramref name="snapshot"/> again, over the current inputs.</summary>
    /// <remarks>
    /// The caller has established that a fresh <see cref="RenderNode.Process(RenderNodeContext)"/> would
    /// record exactly this. Every fragment is recreated rather than reused: a recorded fragment carries the
    /// graph identity of the request that committed it, and metadata resolution writes resolved bounds into
    /// it, so one instance cannot belong to two requests.
    /// </remarks>
    internal void ReplayRecording(
        RenderNodeRecordingSnapshot snapshot,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(snapshot);
        ReplayedRenderFragment[] fragments = snapshot.Fragments
            ?? throw new InvalidOperationException("The recording snapshot cannot be replayed.");

        _cacheDisabled |= snapshot.DisabledRenderCache;
        if (fragments.Length == 0)
        {
            ReplaySlots(snapshot, [], inputs);
            return;
        }

        // Pure scratch - nothing reads it once this method returns - which is what lets it come from a pool.
        // Rent hands each caller a buffer of its own, so a replay nested inside another cannot take the one
        // its caller is still indexing.
        ArrayPool<RenderFragmentReference> pool = ReplayScratchPool;
        RenderFragmentReference[] rented = pool.Rent(fragments.Length);
        try
        {
            // Rent may hand back a longer buffer; slicing keeps an out-of-range slot a bounds failure rather
            // than a read of whatever the previous renter left past the end.
            Span<RenderFragmentReference> replayed = rented.AsSpan(0, fragments.Length);
            _fragments.EnsureCapacity(_fragments.Count + fragments.Length);
            for (int index = 0; index < fragments.Length; index++)
            {
                ReplayedRenderFragment fragment = fragments[index];
                int[] slots = fragment.InputSlots;
                ImmutableArray<RenderFragmentReference> fragmentInputs;
                if (slots.Length == 0)
                {
                    fragmentInputs = [];
                }
                else
                {
                    // Retained as the clone's Inputs, so it gets an array of its own rather than the scratch.
                    var resolved = new RenderFragmentReference[slots.Length];
                    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                        resolved[slotIndex] = ResolveSlot(slots[slotIndex], replayed, inputs);
                    fragmentInputs = ImmutableCollectionsMarshal.AsImmutableArray(resolved);
                }

                RenderFragmentReference reference = fragment.Template.CloneForReplay(fragmentInputs);
                replayed[index] = reference;
                OwnedReferences.Add(reference);
                _fragments.Add(new RecordedRenderFragmentEntry(reference, fragment.Origin, fragment.Role));
                _hasOwnTargetEffectFragment |= IsTargetEffect(reference.Kind);
            }

            ReplaySlots(snapshot, replayed, inputs);
        }
        finally
        {
            pool.Return(rented, clearArray: true);
        }
    }

    private void ReplaySlots(
        RenderNodeRecordingSnapshot snapshot,
        ReadOnlySpan<RenderFragmentReference> replayed,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        foreach (int slot in snapshot.DroppedSlots ?? [])
        {
            (_dropped ??= new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance))
                .Add(ResolveSlot(slot, replayed, inputs));
        }

        int[] publicationSlots = snapshot.PublicationSlots ?? [];
        _publications.EnsureCapacity(_publications.Count + publicationSlots.Length);
        foreach (int slot in publicationSlots)
            _publications.Add(ResolveSlot(slot, replayed, inputs));
    }

    private static RenderFragmentReference ResolveSlot(
        int slot,
        ReadOnlySpan<RenderFragmentReference> replayed,
        IReadOnlyList<RenderFragmentReference> inputs)
        => slot >= 0 ? replayed[slot] : inputs[-slot - 1];

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
                RenderResource resource = _resources![index];
                if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
                    Request.Options.Owner.ResourceRegistry.Rollback(resource);
                else
                    Request.Options.Owner.ResourceRegistry.Release(resource);
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

    private void ValidateRecordedInvariants()
    {
        InvariantScratch scratch = RentScratch();
        try
        {
            HashSet<RenderFragmentReference> reachable = scratch.Reachable;
            HashSet<RenderFragmentReference> fanOutRestricted = scratch.FanOutRestricted;
            foreach (RenderFragmentReference publication in _publications)
                reachable.Add(publication);

            // A fragment's inputs are already owned when it is created and a child's entries are absorbed at
            // its commit, so _fragments is in creation order. Nothing recorded earlier can make a later entry
            // reachable, which is what lets one backward sweep settle reachability and fan-out together.
            bool fanOutViolation = false;
            for (int index = _fragments.Count - 1; index >= 0; index--)
            {
                RenderFragmentReference reference = _fragments[index].Reference;
                if (!reachable.Contains(reference))
                    continue;

                foreach (RenderFragmentReference input in reference.Inputs)
                {
                    reachable.Add(input);

                    // Only a fragment barred from fan-out can fail the check, so the rest never enter the set.
                    if (!input.AllowsFanOut && !fanOutRestricted.Add(input))
                        fanOutViolation = true;
                }
            }

            // The orphan diagnostic keeps precedence over fan-out, so the sweep records rather than throws.
            if (_hasOwnTargetEffectFragment)
                ValidateNoOrphanedTargetEffects(reachable);

            foreach (RenderFragmentReference publication in _publications)
            {
                if (!publication.AllowsFanOut && !fanOutRestricted.Add(publication))
                    fanOutViolation = true;
            }

            if (fanOutViolation)
            {
                throw new InvalidOperationException(
                    "A target-effect render fragment cannot be consumed or published more than once.");
            }
        }
        finally
        {
            ReturnScratch(scratch);
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

    private static InvariantScratch RentScratch()
    {
        InvariantScratch? scratch = t_scratch;
        if (scratch is null)
        {
            t_scratch = scratch = new InvariantScratch();
        }
        else if (scratch.Rented)
        {
            // The sweep runs no user code, so an overlapping rent is not expected. Private sets keep the
            // sharing an allocation win rather than a correctness assumption.
            return new InvariantScratch { Rented = true };
        }

        scratch.Rented = true;
        return scratch;
    }

    private static void ReturnScratch(InvariantScratch scratch)
    {
        int peak = Math.Max(scratch.Reachable.Count, scratch.FanOutRestricted.Count);
        scratch.Reachable.Clear();
        scratch.FanOutRestricted.Clear();

        // Clear keeps the buckets a warm thread already sized, which is the point of pooling. One outsized
        // commit must not pin that capacity for the life of the thread.
        if (peak > RecycledSetSizeLimit)
        {
            scratch.Reachable.TrimExcess();
            scratch.FanOutRestricted.TrimExcess();
        }

        scratch.Rented = false;
    }

    // Drop is not transitive and a parent never receives handles to a child's internal fragments.
    private void ValidateNoOrphanedTargetEffects(
        HashSet<RenderFragmentReference> reachable)
    {
        foreach (RecordedRenderFragmentEntry entry in _fragments)
        {
            RenderFragmentReference reference = entry.Reference;
            if (!ReferenceEquals(entry.Origin, _origin)
                || !IsTargetEffect(reference.Kind)
                || reachable.Contains(reference)
                || _dropped?.Contains(reference) == true)
            {
                continue;
            }

            throw new InvalidOperationException(
                "A recorded target-effect fragment was neither published nor consumed. "
                + "Publish it, wrap it in a fragment you publish, or call Drop to abandon it "
                + $"deliberately. Fragment kind: {reference.Kind}; recorded by: "
                + $"{entry.Origin.GetType().FullName}.");
        }
    }

    private static bool IsTargetEffect(RenderFragmentKind kind)
        => kind is RenderFragmentKind.TargetCommand
            or RenderFragmentKind.RawTargetCommand
            or RenderFragmentKind.TargetScope
            or RenderFragmentKind.RawTargetScope
            or RenderFragmentKind.TargetLayerScope;

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

    private sealed class InvariantScratch
    {
        public HashSet<RenderFragmentReference> Reachable { get; } = new(ReferenceEqualityComparer.Instance);

        public HashSet<RenderFragmentReference> FanOutRestricted { get; } =
            new(ReferenceEqualityComparer.Instance);

        public bool Rented { get; set; }
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

    void Commit(in NodeRecordingCommit commit);
}

/// <summary>What one recording hands to whoever absorbs it.</summary>
/// <remarks>
/// A value, not an object: it is created once per node visit, read once, and never stored, so the heap
/// object it used to be was pure per-frame cost. Every member is already an immutable array, which is what
/// lets the copy stay a handful of references.
/// </remarks>
internal readonly record struct NodeRecordingCommit(
    ImmutableArray<RecordedRenderFragmentEntry> Fragments,
    ImmutableArray<RenderFragmentReference> Publications,
    ImmutableArray<RenderResource> Resources,
    ImmutableArray<RecordedNestedRenderRequest> NestedRequests,
    ImmutableArray<BuiltInBackdropBinding> BuiltInBackdropBindings,
    ImmutableArray<RenderFragmentReference> Dropped);

/// <summary>One hit test a recording read, and what it answered.</summary>
internal readonly record struct RecordedHitTestRead(
    RenderFragmentReference Reference,
    Point Point,
    bool Concrete,
    bool Result);

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
