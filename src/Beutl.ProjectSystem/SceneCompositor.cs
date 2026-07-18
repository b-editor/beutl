using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneCompositor : ICompositor
{
    private readonly object _resourceCacheGate = new();
    private readonly object _frameEvaluationGate = new();
    private readonly object _auxiliaryEvaluationGate = new();
    private readonly ConditionalWeakTable<EngineObject, ResourceSlot> _frameResourceCache = new();
    private readonly ConditionalWeakTable<EngineObject, ResourceSlot> _auxiliaryResourceCache = new();
    private readonly ConditionalWeakTable<EngineObject, ObjectResourceState> _objectResourceStates = new();
    private readonly ConditionalWeakTable<EngineObject.Resource, ResourceSlot> _resourceOwners = new();
    private readonly AsyncLocal<ResourceOperationChain?> _resourceOperationChain = new();
    private readonly AsyncLocal<bool> _evaluationInProgress = new();
    private long _sceneChildrenGeneration;
    private bool _isDisposed;
    private bool _disposeCompleted;
    private bool _sceneSubscriptionsRemovalClaimed;

    private enum SubscriptionState
    {
        None,
        Adding,
        Added,
        Removing,
    }

    private sealed class ObjectResourceState(EventHandler<HierarchyAttachmentEventArgs> handler)
    {
        public EventHandler<HierarchyAttachmentEventArgs> Handler { get; } = handler;

        public long Generation { get; set; }

        public bool IsDetached { get; set; }

        public SubscriptionState Subscription { get; set; }

        public int SubscriptionOwnerThreadId { get; set; }

        public bool RemoveSubscriptionAfterAdd { get; set; }
    }

    private sealed class ResourceSlot
    {
        public EngineObject.Resource? Resource { get; set; }

        public bool RequiresCleanup { get; set; }

        public bool IsBusy { get; set; }

        public int BusyOwnerThreadId { get; set; }

        public long OperationId { get; set; }
    }

    private sealed class ResourceOperationChain(
        ResourceSlot slot,
        long operationId,
        ResourceOperationChain? parent)
    {
        public ResourceSlot Slot { get; } = slot;

        public long OperationId { get; } = operationId;

        public ResourceOperationChain? Parent { get; } = parent;

        public bool Contains(ResourceSlot candidate, long candidateOperationId)
        {
            for (ResourceOperationChain? current = this; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current.Slot, candidate)
                    && current.OperationId == candidateOperationId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly record struct ResourceCandidate(
        EngineObject Object,
        Element Parent,
        ObjectResourceState State,
        long Generation,
        long SceneGeneration);

    private sealed class PublicationToken(
        ResourceCandidate candidate,
        EngineObject.Resource resource,
        IReadOnlyList<PublicationToken> dependencies,
        bool hasUntrackedDependency)
    {
        public ResourceCandidate Candidate { get; } = candidate;

        public EngineObject.Resource Resource { get; } = resource;

        public IReadOnlyList<PublicationToken> Dependencies { get; } = dependencies;

        public bool HasUntrackedDependency { get; } = hasUntrackedDependency;
    }

    private sealed class TrackedFlowEntry(
        EngineObject.Resource resource,
        PublicationToken? token)
    {
        public EngineObject.Resource Resource { get; } = resource;

        public PublicationToken? Token { get; set; } = token;
    }

    private sealed class TrackedResourceFlow : IList<EngineObject.Resource>
    {
        private readonly List<TrackedFlowEntry> _items = [];
        private readonly List<DependencyCapture> _captures = [];

        public EngineObject.Resource this[int index]
        {
            get
            {
                TrackedFlowEntry entry = _items[index];
                CaptureReadDependency(entry);
                return entry.Resource;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                CaptureRemoval(_items[index]);
                var entry = new TrackedFlowEntry(value, null);
                _items[index] = entry;
                RecordPendingProvenance(entry);
            }
        }

        public int Count
        {
            get
            {
                foreach (TrackedFlowEntry entry in _items)
                {
                    CaptureReadDependency(entry);
                }

                return _items.Count;
            }
        }

        public bool IsReadOnly => false;

        public DependencyCapture CaptureDependencies()
        {
            var capture = new DependencyCapture(this);
            _captures.Add(capture);
            return capture;
        }

        public TrackedFlowEntry[] SnapshotEntries() => [.. _items];

        public void Add(EngineObject.Resource item)
        {
            ArgumentNullException.ThrowIfNull(item);
            var entry = new TrackedFlowEntry(item, null);
            _items.Add(entry);
            RecordPendingProvenance(entry);
        }

        public void AddTracked(EngineObject.Resource item, PublicationToken token)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(token);
            _items.Add(new TrackedFlowEntry(item, token));
        }

        public void Clear()
        {
            foreach (TrackedFlowEntry item in _items)
            {
                CaptureRemoval(item);
            }

            _items.Clear();
        }

        public bool Contains(EngineObject.Resource item) => IndexOf(item) >= 0;

        public void CopyTo(EngineObject.Resource[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            foreach (TrackedFlowEntry item in _items)
            {
                CaptureReadDependency(item);
                array[arrayIndex++] = item.Resource;
            }
        }

        public IEnumerator<EngineObject.Resource> GetEnumerator()
        {
            foreach (TrackedFlowEntry item in _items)
            {
                CaptureReadDependency(item);
                yield return item.Resource;
            }
        }

        public int IndexOf(EngineObject.Resource item)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                TrackedFlowEntry entry = _items[i];
                CaptureReadDependency(entry);
                if (ReferenceEquals(entry.Resource, item))
                    return i;
            }

            return -1;
        }

        public void Insert(int index, EngineObject.Resource item)
        {
            ArgumentNullException.ThrowIfNull(item);
            var entry = new TrackedFlowEntry(item, null);
            _items.Insert(index, entry);
            RecordPendingProvenance(entry);
        }

        public bool Remove(EngineObject.Resource item)
        {
            int index = IndexOf(item);
            if (index < 0)
                return false;

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            TrackedFlowEntry item = _items[index];
            _items.RemoveAt(index);
            CaptureRemoval(item);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();

        private void CaptureRemoval(TrackedFlowEntry entry)
            => RecordDependency(entry);

        private void CaptureReadDependency(TrackedFlowEntry entry)
            => RecordDependency(entry);

        private void RecordDependency(TrackedFlowEntry entry)
        {
            foreach (DependencyCapture capture in _captures)
            {
                capture.AddDependency(entry);
            }
        }

        private void RecordPendingProvenance(TrackedFlowEntry entry)
        {
            foreach (DependencyCapture capture in _captures)
            {
                capture.AddPendingProvenance(entry);
            }
        }

        private void EndCapture(DependencyCapture capture)
        {
            int index = _captures.LastIndexOf(capture);
            if (index >= 0)
            {
                _captures.RemoveAt(index);
            }
        }

        public sealed class DependencyCapture(TrackedResourceFlow owner) : IDisposable
        {
            private readonly List<PublicationToken> _dependencies = [];
            private readonly List<TrackedFlowEntry> _pendingProvenance = [];
            private bool _isDisposed;

            public IReadOnlyList<PublicationToken> Dependencies => _dependencies;

            public bool HasUntrackedDependency { get; private set; }

            internal void AddDependency(TrackedFlowEntry entry)
            {
                if (entry.Token is { } token)
                {
                    if (!_dependencies.Contains(token))
                    {
                        _dependencies.Add(token);
                    }
                }
                else if (!_pendingProvenance.Contains(entry))
                {
                    HasUntrackedDependency = true;
                }
            }

            internal void AddPendingProvenance(TrackedFlowEntry entry)
            {
                if (!_pendingProvenance.Contains(entry))
                {
                    _pendingProvenance.Add(entry);
                }
            }

            public void BindPendingProvenance(PublicationToken token)
            {
                foreach (TrackedFlowEntry entry in _pendingProvenance)
                {
                    entry.Token ??= token;
                }
            }

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                owner.EndCapture(this);
            }
        }
    }

    private readonly record struct SlotCleanup(
        ResourceSlot Slot,
        long OperationId,
        EngineObject.Resource Resource);

    private sealed class CleanupBatch
    {
        public List<SlotCleanup> Slots { get; } = [];

        public List<EngineObject.Resource> Resources { get; } = [];

        public void Add(ResourceSlot slot, long operationId, EngineObject.Resource resource)
        {
            Slots.Add(new SlotCleanup(slot, operationId, resource));
            Resources.Add(resource);
        }
    }

    // Mute flags are read live from the layers inside the snapshot, so only the
    // lookup shape (membership, ZIndex) and HasSolo require invalidation.
    private volatile LayerSnapshot? _layerSnapshot;

    public SceneCompositor(Scene scene)
        : this(scene, RenderIntent.Preview)
    {
    }

    public SceneCompositor(Scene scene, RenderIntent renderIntent)
    {
        RenderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        Scene = scene;
        Scene.Children.CollectionChanged += OnSceneChildrenCollectionChanged;
        Scene.Layers.CollectionChanged += OnLayersCollectionChanged;
        Scene.Layers.Attached += OnLayerAttached;
        Scene.Layers.Detached += OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            layer.PropertyChanged += OnLayerPropertyChanged;
        }
    }

    public Scene Scene { get; }

    public RenderIntent RenderIntent { get; }

    public bool DisableResourceShare { get; init; }

    public bool ForceOriginalSource { get; init; }

    private sealed class CompositorContext : CompositionContext, ISceneCompositionContext
    {
        private readonly SceneCompositor _compositor;

        public CompositorContext(TimeSpan time,
            SceneCompositor compositor,
            IList<EngineObject.Resource> flow,
            IList<Element> currentElements,
            CompositionTarget target,
            RenderPullPurpose pullPurpose,
            long sceneGeneration)
            : base(time, compositor.RenderIntent, pullPurpose)
        {
            _compositor = compositor;
            CurrentElements = currentElements;
            Target = target;
            SceneGeneration = sceneGeneration;
            Flow = flow;
            DisableResourceShare = compositor.DisableResourceShare;
            PreferProxy = !compositor.ForceOriginalSource
                && GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode == PreviewSourceMode.PreferProxy;
            PreferredProxyPreset = ToPreset(GlobalConfiguration.Instance.ProxyStoreConfig.DefaultPreset);
        }

        public IList<Element> CurrentElements { get; set; }

        public CompositionTarget Target { get; set; }

        public long SceneGeneration { get; }

        private static ProxyPreset ToPreset(int value)
        {
            return Enum.IsDefined(typeof(ProxyPreset), value)
                ? (ProxyPreset)value
                : ProxyPreset.Quarter;
        }

        public void EvaluateElementIntoFlow(Element element)
        {
            using var tmpObjects = new PooledList<EngineObject>();
            _compositor.CollectResourcesFromElement(element, this, tmpObjects);
        }
    }

    public CompositionFrame EvaluateGraphics(TimeSpan time)
        => EvaluateGraphics(time, RenderPullPurpose.Frame);

    public CompositionFrame EvaluateGraphics(TimeSpan time, RenderPullPurpose pullPurpose)
    {
        ThrowIfDisposed();
        pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        return EvaluateSerialized(
            pullPurpose,
            () => EvaluateGraphicsCore(time, pullPurpose));
    }

    private CompositionFrame EvaluateGraphicsCore(TimeSpan time, RenderPullPurpose pullPurpose)
    {
        long sceneGeneration = CaptureSceneGeneration();
        using var currentElements = new PooledList<Element>();
        SortLayers(time, currentElements, CompositionTarget.Graphics);

        using var tmpObjects = new PooledList<EngineObject>();
        var flow = new TrackedResourceFlow();
        var pendingResources = new TrackedResourceFlow();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(
            time, this, flow, currentElements, CompositionTarget.Graphics, pullPurpose, sceneGeneration);

        // Use an index loop because currentElements can change during iteration.
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            // Collect the EngineObjects.
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);
            CopyValidResources(flow, pullPurpose, pendingResources);
        }

        CopyValidResources(pendingResources, pullPurpose, allResources);

        return new CompositionFrame(
            [.. allResources],
            new(time, TimeSpan.FromTicks(1)),
            Scene.FrameSize,
            RenderIntent,
            pullPurpose);
    }

    public CompositionFrame EvaluateAudio(TimeRange timeRange)
        => EvaluateAudio(timeRange, RenderPullPurpose.Frame);

    public CompositionFrame EvaluateAudio(TimeRange timeRange, RenderPullPurpose pullPurpose)
    {
        ThrowIfDisposed();
        pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        return EvaluateSerialized(
            pullPurpose,
            () => EvaluateAudioCore(timeRange, pullPurpose));
    }

    private CompositionFrame EvaluateAudioCore(TimeRange timeRange, RenderPullPurpose pullPurpose)
    {
        long sceneGeneration = CaptureSceneGeneration();
        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements, CompositionTarget.Audio);

        using var tmpObjects = new PooledList<EngineObject>();
        var flow = new TrackedResourceFlow();
        var pendingResources = new TrackedResourceFlow();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(
            timeRange.Start, this, flow, currentElements, CompositionTarget.Audio, pullPurpose, sceneGeneration);

        // Use an index loop because currentElements can change during iteration.
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            // Collect the EngineObjects.
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);
            CopyValidResources(flow, pullPurpose, pendingResources);
        }

        CopyValidResources(pendingResources, pullPurpose, allResources);

        return new CompositionFrame(
            [.. allResources], timeRange, Scene.FrameSize, RenderIntent, pullPurpose);
    }

    private T EvaluateSerialized<T>(RenderPullPurpose pullPurpose, Func<T> callback)
    {
        if (_evaluationInProgress.Value)
        {
            throw new InvalidOperationException(
                "A composition resource callback cannot synchronously re-enter the same SceneCompositor evaluation.");
        }

        object gate = pullPurpose == RenderPullPurpose.Frame
            ? _frameEvaluationGate
            : _auxiliaryEvaluationGate;
        lock (gate)
        {
            ThrowIfDisposed();
            _evaluationInProgress.Value = true;
            try
            {
                return callback();
            }
            finally
            {
                _evaluationInProgress.Value = false;
            }
        }
    }

    private long CaptureSceneGeneration()
    {
        lock (_resourceCacheGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _sceneChildrenGeneration;
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_resourceCacheGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }
    }

    private void CollectResourcesFromElement(
        Element element, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        var flow = new TrackedResourceFlow();
        using var candidates = new PooledList<ResourceCandidate>();
        var oldFlow = context.Flow;
        context.Flow = flow;
        try
        {
            tmpObjects.Clear();
            element.CollectObjects(context.Target, tmpObjects);
            foreach (EngineObject obj in tmpObjects.Span)
            {
                if (TryPrepareResourceCandidate(
                        obj,
                        element,
                        context.SceneGeneration,
                        out ResourceCandidate candidate))
                {
                    candidates.Add(candidate);
                }
            }

            foreach (ResourceCandidate candidate in candidates.Span)
            {
                PublicationToken? token = null;
                using (TrackedResourceFlow.DependencyCapture capture = flow.CaptureDependencies())
                {
                    if (TryGetOrCreateResource(candidate, context, out EngineObject.Resource? resource)
                        && resource != null)
                    {
                        token = new PublicationToken(
                            candidate,
                            resource,
                            [.. capture.Dependencies],
                            capture.HasUntrackedDependency);
                        capture.BindPendingProvenance(token);
                    }
                }

                if (token != null)
                {
                    TryPublishResource(token, token.Resource, context.PullPurpose, flow);
                }
            }

            CopyValidResources(flow, context.PullPurpose, oldFlow);
        }
        finally
        {
            context.Flow = oldFlow;
        }
    }

    private bool TryPublishResource(
        PublicationToken token,
        EngineObject.Resource resource,
        RenderPullPurpose pullPurpose,
        IList<EngineObject.Resource>? destination)
    {
        if (destination == null)
            return false;

        lock (_resourceCacheGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!IsPublicationTokenValid(
                    token,
                    pullPurpose,
                    new HashSet<PublicationToken>(ReferenceEqualityComparer.Instance))
                || resource.IsDisposed)
            {
                return false;
            }

            if (destination is TrackedResourceFlow tracked)
            {
                tracked.AddTracked(resource, token);
            }
            else
            {
                destination.Add(resource);
            }

            return true;
        }
    }

    private void CopyValidResources(
        TrackedResourceFlow source,
        RenderPullPurpose pullPurpose,
        IList<EngineObject.Resource>? destination)
    {
        foreach (TrackedFlowEntry entry in source.SnapshotEntries())
        {
            if (entry.Token != null && !entry.Resource.IsDisposed)
            {
                TryPublishResource(entry.Token, entry.Resource, pullPurpose, destination);
            }
        }
    }

    private bool IsPublicationTokenValid(
        PublicationToken token,
        RenderPullPurpose pullPurpose,
        HashSet<PublicationToken> visited)
    {
        if (!visited.Add(token))
            return true;

        if (token.HasUntrackedDependency)
            return false;

        ResourceCandidate candidate = token.Candidate;
        EngineObject.Resource resource = token.Resource;
        if (candidate.State.Generation != candidate.Generation
            || candidate.State.IsDetached
            || candidate.SceneGeneration != _sceneChildrenGeneration
            || !IsAttachedToElement(candidate.Object, candidate.Parent)
            || !IsElementAttachedToScene(candidate.Parent)
            || resource.IsDisposed)
        {
            return false;
        }

        ConditionalWeakTable<EngineObject, ResourceSlot> cache = GetResourceCache(pullPurpose);
        if (!cache.TryGetValue(candidate.Object, out ResourceSlot? slot)
            || slot.IsBusy
            || slot.RequiresCleanup
            || !ReferenceEquals(slot.Resource, resource))
        {
            return false;
        }

        foreach (PublicationToken dependency in token.Dependencies)
        {
            if (!IsPublicationTokenValid(dependency, pullPurpose, visited))
                return false;
        }

        return true;
    }

    private bool TryPrepareResourceCandidate(
        EngineObject obj,
        Element parent,
        long sceneGeneration,
        out ResourceCandidate candidate)
    {
        candidate = default;
        ObjectResourceState? state = null;
        int currentThreadId = Environment.CurrentManagedThreadId;
        while (true)
        {
            if (!IsAttachedToElement(obj, parent)
                || !IsElementAttachedToScene(parent))
            {
                return false;
            }

            long generation;
            lock (_resourceCacheGate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (_sceneChildrenGeneration != sceneGeneration)
                {
                    return false;
                }

                state ??= GetOrCreateObjectState(obj);
                if (state.IsDetached
                    && IsObjectResourceBusy(
                        obj, state, out int ownerThreadId, out ResourceSlot? busySlot))
                {
                    ThrowIfReentrantWait(busySlot, ownerThreadId, currentThreadId);
                    Monitor.Wait(_resourceCacheGate);
                    continue;
                }

                // A new snapshot may start a post-detach generation only while the object is attached to the
                // element that produced that snapshot. Later callbacks consume the captured generation token;
                // they never clear IsDetached on behalf of an older snapshot.
                state.IsDetached = false;
                generation = state.Generation;
            }

            EnsureDetachedHandler(obj, state, generation);
            if (!IsAttachedToElement(obj, parent)
                || !IsElementAttachedToScene(parent))
            {
                return false;
            }

            lock (_resourceCacheGate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (state.Generation != generation
                    || state.IsDetached
                    || _sceneChildrenGeneration != sceneGeneration)
                {
                    return false;
                }
            }

            candidate = new ResourceCandidate(obj, parent, state, generation, sceneGeneration);
            return true;
        }
    }

    private bool TryGetOrCreateResource(
        ResourceCandidate candidate,
        CompositionContext context,
        out EngineObject.Resource? result)
    {
        result = null;
        if (!IsAttachedToElement(candidate.Object, candidate.Parent)
            || !IsElementAttachedToScene(candidate.Parent))
        {
            return false;
        }

        EngineObject obj = candidate.Object;
        ObjectResourceState state = candidate.State;
        long generation = candidate.Generation;
        int currentThreadId = Environment.CurrentManagedThreadId;
        ResourceSlot slot;
        EngineObject.Resource? resource;
        bool create;
        long operationId;
        while (true)
        {
            CleanupBatch? pendingCleanup = null;
            lock (_resourceCacheGate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (state.Generation != generation
                    || state.IsDetached
                    || _sceneChildrenGeneration != candidate.SceneGeneration)
                {
                    return false;
                }

                slot = GetOrCreateResourceSlot(obj, context.PullPurpose);
                while (slot.IsBusy)
                {
                    ThrowIfReentrantWait(slot, slot.BusyOwnerThreadId, currentThreadId);
                    Monitor.Wait(_resourceCacheGate);
                    ObjectDisposedException.ThrowIf(_isDisposed, this);
                    if (state.Generation != generation
                        || state.IsDetached
                        || _sceneChildrenGeneration != candidate.SceneGeneration)
                    {
                        return false;
                    }
                }

                resource = slot.Resource;
                if (resource?.IsDisposed == true)
                {
                    ReleaseSlotResource(slot);
                    resource = null;
                }

                if (slot.RequiresCleanup && resource != null)
                {
                    pendingCleanup = new CleanupBatch();
                    ClaimIdleSlotForCleanup(slot, pendingCleanup, currentThreadId);
                }
                else
                {
                    create = resource == null;
                    slot.IsBusy = true;
                    slot.BusyOwnerThreadId = currentThreadId;
                    operationId = ++slot.OperationId;
                    break;
                }
            }

            Exception? cleanupFailure = ExecuteCleanupBatch(pendingCleanup!);
            ThrowFirstFailure(cleanupFailure);
        }

        ResourceOperationChain? previousOperation = _resourceOperationChain.Value;
        _resourceOperationChain.Value = new ResourceOperationChain(slot, operationId, previousOperation);
        try
        {
            // ToResource and Update are public/virtual extension points. They must never run while the cache gate is
            // held. The logical operation chain remains active through cleanup so Task.Run-based synchronous
            // re-entry fails before waiting for a slot that its parent callback owns.
            Exception? callbackFailure = null;
            try
            {
                if (create)
                {
                    resource = obj.ToResource(context);
                }
                else
                {
                    bool _ = false;
                    resource!.Update(obj, context, ref _);
                }
            }
            catch (Exception ex)
            {
                callbackFailure = ex;
            }

            bool attachmentValid = IsAttachedToElement(obj, candidate.Parent)
                && IsElementAttachedToScene(candidate.Parent);
            result = CompleteResourceOperation(
                state,
                slot,
                generation,
                operationId,
                create,
                resource,
                callbackFailure,
                attachmentValid);
            return true;
        }
        finally
        {
            _resourceOperationChain.Value = previousOperation;
        }
    }

    private static bool IsAttachedToElement(EngineObject obj, Element parent)
        => ReferenceEquals(((IHierarchical)obj).HierarchicalParent, parent);

    private bool IsElementAttachedToScene(Element element)
        => ReferenceEquals(((IHierarchical)element).HierarchicalParent, Scene);

    private ConditionalWeakTable<EngineObject, ResourceSlot> GetResourceCache(
        RenderPullPurpose pullPurpose)
    {
        return pullPurpose switch
        {
            RenderPullPurpose.Frame => _frameResourceCache,
            RenderPullPurpose.Auxiliary => _auxiliaryResourceCache,
            _ => throw new ArgumentOutOfRangeException(nameof(pullPurpose)),
        };
    }

    private ObjectResourceState GetOrCreateObjectState(EngineObject obj)
    {
        if (_objectResourceStates.TryGetValue(obj, out ObjectResourceState? state))
        {
            return state;
        }

        var weakRef = new WeakReference<SceneCompositor>(this);
        EventHandler<HierarchyAttachmentEventArgs> handler = (sender, _) =>
        {
            if (sender is EngineObject senderObj
                && weakRef.TryGetTarget(out SceneCompositor? compositor))
            {
                compositor.HandleObjectDetached(senderObj);
            }
        };
        state = new ObjectResourceState(handler);
        _objectResourceStates.Add(obj, state);
        return state;
    }

    private ResourceSlot GetOrCreateResourceSlot(EngineObject obj, RenderPullPurpose pullPurpose)
    {
        ConditionalWeakTable<EngineObject, ResourceSlot> cache = GetResourceCache(pullPurpose);
        if (!cache.TryGetValue(obj, out ResourceSlot? slot))
        {
            slot = new ResourceSlot();
            cache.Add(obj, slot);
        }

        return slot;
    }

    private void EnsureDetachedHandler(
        EngineObject obj,
        ObjectResourceState state,
        long generation)
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        while (true)
        {
            lock (_resourceCacheGate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                ThrowIfGenerationInvalidated(state, generation);
                if (state.Subscription == SubscriptionState.Added)
                {
                    return;
                }

                if (state.Subscription is SubscriptionState.Adding or SubscriptionState.Removing)
                {
                    ThrowIfReentrantWait(state.SubscriptionOwnerThreadId, currentThreadId);
                    Monitor.Wait(_resourceCacheGate);
                    continue;
                }

                state.Subscription = SubscriptionState.Adding;
                state.SubscriptionOwnerThreadId = currentThreadId;
            }

            IHierarchicalRoot? rootBefore = ((IHierarchical)obj).HierarchicalRoot;
            Exception? addFailure = null;
            try
            {
                obj.DetachedFromHierarchy += state.Handler;
            }
            catch (Exception ex)
            {
                addFailure = ex;
            }

            IHierarchicalRoot? rootAfter = ((IHierarchical)obj).HierarchicalRoot;
            bool missedDetach = rootBefore != null && !ReferenceEquals(rootBefore, rootAfter);
            bool removeSubscription = false;
            bool invalidated;
            bool disposedAtCompletion;
            CleanupBatch cleanup = new();
            lock (_resourceCacheGate)
            {
                state.SubscriptionOwnerThreadId = 0;
                if (addFailure != null)
                {
                    state.Subscription = SubscriptionState.None;
                    state.RemoveSubscriptionAfterAdd = false;
                }
                else if (_isDisposed || state.RemoveSubscriptionAfterAdd)
                {
                    state.Subscription = SubscriptionState.Removing;
                    state.SubscriptionOwnerThreadId = currentThreadId;
                    state.RemoveSubscriptionAfterAdd = false;
                    removeSubscription = true;
                }
                else
                {
                    state.Subscription = SubscriptionState.Added;
                    if (missedDetach && state.Generation == generation && !state.IsDetached)
                    {
                        InvalidateObjectResources(obj, state, cleanup, currentThreadId);
                    }
                }

                invalidated = state.Generation != generation || state.IsDetached;
                disposedAtCompletion = _isDisposed;
                Monitor.PulseAll(_resourceCacheGate);
            }

            Exception? cleanupFailure = ExecuteCleanupBatch(cleanup);
            Exception? removeFailure = null;
            if (removeSubscription)
            {
                try
                {
                    obj.DetachedFromHierarchy -= state.Handler;
                }
                catch (Exception ex)
                {
                    removeFailure = ex;
                }
                finally
                {
                    lock (_resourceCacheGate)
                    {
                        state.Subscription = SubscriptionState.None;
                        state.SubscriptionOwnerThreadId = 0;
                        Monitor.PulseAll(_resourceCacheGate);
                    }
                }
            }

            if (addFailure != null || removeFailure != null || cleanupFailure != null)
            {
                ThrowFirstFailure(addFailure, removeFailure, cleanupFailure);
            }

            ObjectDisposedException.ThrowIf(disposedAtCompletion, this);
            if (invalidated)
            {
                throw new InvalidOperationException(
                    "The engine object detached while its composition subscription was being installed.");
            }

            return;
        }
    }

    private EngineObject.Resource CompleteResourceOperation(
        ObjectResourceState state,
        ResourceSlot slot,
        long generation,
        long operationId,
        bool create,
        EngineObject.Resource? resource,
        Exception? callbackFailure,
        bool attachmentValid)
    {
        bool publish = false;
        Exception? invalidationFailure = null;
        Exception? contractFailure = null;
        CleanupBatch cleanup = new();
        lock (_resourceCacheGate)
        {
            if (!slot.IsBusy || slot.OperationId != operationId)
            {
                throw new InvalidOperationException("The composition resource operation lost ownership of its cache slot.");
            }

            if (_isDisposed)
            {
                invalidationFailure = new ObjectDisposedException(nameof(SceneCompositor));
            }
            else if (state.Generation != generation
                || state.IsDetached
                || !attachmentValid)
            {
                invalidationFailure = new InvalidOperationException(
                    "The engine object detached while its composition resource operation was in progress.");
            }

            // A scene membership change does not detach this object. Accept the callback result into
            // its cache; the candidate's scene-generation token prevents the stale snapshot from
            // publishing it into the current frame.

            if (callbackFailure == null && resource == null)
            {
                callbackFailure = new InvalidOperationException(
                    "EngineObject.ToResource returned null.");
            }

            bool resourceOwnedElsewhere = false;
            if (create && resource != null)
            {
                resourceOwnedElsewhere = _resourceOwners.TryGetValue(resource, out ResourceSlot? owner)
                    && !ReferenceEquals(owner, slot);
                if (callbackFailure == null
                    && invalidationFailure == null
                    && resourceOwnedElsewhere)
                {
                    contractFailure = new InvalidOperationException(
                        "EngineObject.ToResource must return a distinct Resource instance for each "
                        + "SceneCompositor cache slot; the returned instance is already owned or being disposed.");
                }
            }

            if (callbackFailure == null
                && invalidationFailure == null
                && contractFailure == null
                && resource is { IsDisposed: false })
            {
                if (create)
                {
                    slot.Resource = resource;
                    _resourceOwners.Add(resource, slot);
                }

                slot.RequiresCleanup = false;
                publish = true;
                MarkSlotIdle(slot);
            }
            else
            {
                // A newly acquired resource remains attached to this slot until the complete ownership graph has
                // been reserved. An in-place Update failure likewise leaves the previous resource attached and
                // marks it unusable, so a rejected cleanup can be retried without losing its deterministic owner.
                if (create
                    && resource is { IsDisposed: false }
                    && !resourceOwnedElsewhere)
                {
                    slot.Resource = resource;
                    _resourceOwners.Add(resource, slot);
                }

                if (slot.Resource?.IsDisposed == true)
                {
                    ReleaseSlotResource(slot);
                }

                if (slot.Resource is { } resourceToDispose)
                {
                    slot.RequiresCleanup = true;
                    slot.BusyOwnerThreadId = Environment.CurrentManagedThreadId;
                    cleanup.Add(slot, operationId, resourceToDispose);
                }
                else
                {
                    MarkSlotIdle(slot);
                }
            }

            Monitor.PulseAll(_resourceCacheGate);
        }

        Exception? cleanupFailure = ExecuteCleanupBatch(cleanup);
        if (publish)
        {
            return resource!;
        }

        // The resource callback is the operation's primary work. If it succeeded but its generation was
        // invalidated, that invalidation remains the primary outcome; cleanup failures must not mask either.
        ThrowFirstFailure(callbackFailure, invalidationFailure, contractFailure, cleanupFailure);
        throw new InvalidOperationException("The composition resource operation did not produce a resource.");
    }

    private void HandleObjectDetached(EngineObject obj)
    {
        CleanupBatch cleanup = new();
        lock (_resourceCacheGate)
        {
            if (!_objectResourceStates.TryGetValue(obj, out ObjectResourceState? state))
            {
                return;
            }

            InvalidateObjectResources(
                obj, state, cleanup, Environment.CurrentManagedThreadId);
        }

        Exception? failure = ExecuteCleanupBatch(cleanup);
        ThrowFirstFailure(failure);
    }

    private void InvalidateObjectResources(
        EngineObject obj,
        ObjectResourceState state,
        CleanupBatch cleanup,
        int cleanupOwnerThreadId)
    {
        state.Generation++;
        state.IsDetached = true;
        CollectIdleResourceForCleanup(
            _frameResourceCache, obj, cleanup, cleanupOwnerThreadId);
        CollectIdleResourceForCleanup(
            _auxiliaryResourceCache, obj, cleanup, cleanupOwnerThreadId);
        Monitor.PulseAll(_resourceCacheGate);
    }

    private void CollectIdleResourceForCleanup(
        ConditionalWeakTable<EngineObject, ResourceSlot> cache,
        EngineObject obj,
        CleanupBatch cleanup,
        int cleanupOwnerThreadId)
    {
        if (!cache.TryGetValue(obj, out ResourceSlot? slot)
            || slot.IsBusy
            || slot.Resource == null)
        {
            return;
        }

        ClaimIdleSlotForCleanup(slot, cleanup, cleanupOwnerThreadId);
    }

    private static void ClaimIdleSlotForCleanup(
        ResourceSlot slot,
        CleanupBatch cleanup,
        int cleanupOwnerThreadId)
    {
        EngineObject.Resource resource = slot.Resource
            ?? throw new InvalidOperationException("A resource cleanup slot has no owned resource.");
        slot.RequiresCleanup = true;
        slot.IsBusy = true;
        slot.BusyOwnerThreadId = cleanupOwnerThreadId;
        long operationId = ++slot.OperationId;
        cleanup.Add(slot, operationId, resource);
    }

    private EngineObject.Resource? ReleaseSlotResource(ResourceSlot slot)
    {
        EngineObject.Resource? resource = slot.Resource;
        slot.Resource = null;
        slot.RequiresCleanup = false;
        if (resource != null
            && _resourceOwners.TryGetValue(resource, out ResourceSlot? owner)
            && ReferenceEquals(owner, slot))
        {
            _resourceOwners.Remove(resource);
        }

        return resource;
    }

    private Exception? ExecuteCleanupBatch(CleanupBatch cleanup)
    {
        if (cleanup.Resources.Count == 0)
            return null;

        ResourceOperationChain? previousOperation = _resourceOperationChain.Value;
        ResourceOperationChain? cleanupOperations = previousOperation;
        foreach (SlotCleanup item in cleanup.Slots)
        {
            cleanupOperations = new ResourceOperationChain(
                item.Slot,
                item.OperationId,
                cleanupOperations);
        }

        _resourceOperationChain.Value = cleanupOperations;
        var transaction = new CleanupTransaction(this, cleanup);
        Exception? failure = null;
        try
        {
            transaction.Dispose();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            _resourceOperationChain.Value = previousOperation;
            if (!transaction.ReservationCommitted)
            {
                // A rejected coordinator owns nothing; its finalizer must not retry the caller-owned transaction.
                GC.SuppressFinalize(transaction);
            }
        }

        if (cleanup.Slots.Count > 0)
        {
            lock (_resourceCacheGate)
            {
                foreach (SlotCleanup item in cleanup.Slots)
                {
                    if (item.Slot.IsBusy && item.Slot.OperationId == item.OperationId)
                    {
                        MarkSlotIdle(item.Slot);
                    }
                }

                Monitor.PulseAll(_resourceCacheGate);
            }
        }

        return failure;
    }

    private void CommitCleanupBatch(CleanupBatch cleanup)
    {
        lock (_resourceCacheGate)
        {
            foreach (SlotCleanup item in cleanup.Slots)
            {
                if (!item.Slot.IsBusy
                    || item.Slot.OperationId != item.OperationId
                    || !ReferenceEquals(item.Slot.Resource, item.Resource))
                {
                    throw new InvalidOperationException(
                        "The composition resource cleanup lost ownership of its cache slot.");
                }
            }

            foreach (SlotCleanup item in cleanup.Slots)
            {
                ReleaseSlotResource(item.Slot);
            }
        }
    }

    private sealed class CleanupTransaction(
        SceneCompositor owner,
        CleanupBatch cleanup) : EngineObject.Resource
    {
        private GeneratedResourceCleanupContext? _cleanupContext;

        public bool ReservationCommitted { get; private set; }

        protected override void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                _cleanupContext = context;
                foreach (EngineObject.Resource resource in cleanup.Resources)
                {
                    context.Reserve(resource);
                }
            }

            base.PrepareGeneratedResourceCleanupCore(disposing, context);
        }

        protected override void RollbackGeneratedResourceCleanupCore()
        {
            _cleanupContext = null;
            base.RollbackGeneratedResourceCleanupCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GeneratedResourceCleanupContext context = _cleanupContext
                    ?? throw new InvalidOperationException(
                        "The composition resources were not reserved before cleanup.");
                _cleanupContext = null;
                owner.CommitCleanupBatch(cleanup);
                ReservationCommitted = true;
                foreach (EngineObject.Resource resource in cleanup.Resources)
                {
                    context.DisposeOwned(resource);
                }
            }

            base.Dispose(disposing);
        }
    }

    private bool IsObjectResourceBusy(
        EngineObject obj,
        ObjectResourceState state,
        out int ownerThreadId,
        out ResourceSlot? busySlot)
    {
        if (state.Subscription is SubscriptionState.Adding or SubscriptionState.Removing)
        {
            ownerThreadId = state.SubscriptionOwnerThreadId;
            busySlot = null;
            return true;
        }

        if (_frameResourceCache.TryGetValue(obj, out ResourceSlot? frameSlot) && frameSlot.IsBusy)
        {
            ownerThreadId = frameSlot.BusyOwnerThreadId;
            busySlot = frameSlot;
            return true;
        }

        if (_auxiliaryResourceCache.TryGetValue(obj, out ResourceSlot? auxiliarySlot) && auxiliarySlot.IsBusy)
        {
            ownerThreadId = auxiliarySlot.BusyOwnerThreadId;
            busySlot = auxiliarySlot;
            return true;
        }

        ownerThreadId = 0;
        busySlot = null;
        return false;
    }

    private void ThrowIfReentrantWait(
        ResourceSlot? slot,
        int ownerThreadId,
        int currentThreadId)
    {
        if (ownerThreadId == currentThreadId
            || (slot != null
                && _resourceOperationChain.Value?.Contains(slot, slot.OperationId) == true))
        {
            throw new InvalidOperationException(
                "A composition resource callback cannot synchronously re-enter the same resource operation.");
        }
    }

    private static void ThrowIfReentrantWait(int ownerThreadId, int currentThreadId)
    {
        if (ownerThreadId == currentThreadId)
        {
            throw new InvalidOperationException(
                "A composition resource callback cannot synchronously re-enter the same resource operation.");
        }
    }

    private static void ThrowIfGenerationInvalidated(ObjectResourceState state, long generation)
    {
        if (state.Generation != generation || state.IsDetached)
        {
            throw new InvalidOperationException(
                "The engine object detached before its composition resource operation could start.");
        }
    }

    private static void MarkSlotIdle(ResourceSlot slot)
    {
        slot.IsBusy = false;
        slot.BusyOwnerThreadId = 0;
    }

    private static void TryCleanup(Action action, ref Exception? failure)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }
    }

    private static void ThrowFirstFailure(params Exception?[] failures)
    {
        Exception? failure = failures.FirstOrDefault(item => item != null);
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    // timeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(TimeSpan time, PooledList<Element> currentElements, CompositionTarget target)
    {
        LayerSnapshot snapshot = GetLayerSnapshot();
        if (snapshot.ByZIndex.Count == 0)
        {
            foreach (Element item in Scene.Children)
            {
                if (item.IsEnabled && item.Range.Contains(time))
                {
                    currentElements.OrderedAdd(item, x => x.ZIndex);
                }
            }

            return;
        }

        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled || !item.Range.Contains(time)) continue;
            if (ShouldSkipLayer(item.ZIndex, target, snapshot.HasSolo, snapshot.ByZIndex)) continue;
            currentElements.OrderedAdd(item, x => x.ZIndex);
        }
    }

    // timeRangeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(TimeRange timeRange, PooledList<Element> currentElements, CompositionTarget target)
    {
        LayerSnapshot snapshot = GetLayerSnapshot();
        if (snapshot.ByZIndex.Count == 0)
        {
            foreach (Element item in Scene.Children)
            {
                if (item.IsEnabled && item.Range.Intersects(timeRange))
                {
                    currentElements.OrderedAdd(item, x => x.ZIndex);
                }
            }

            return;
        }

        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled || !item.Range.Intersects(timeRange)) continue;
            if (ShouldSkipLayer(item.ZIndex, target, snapshot.HasSolo, snapshot.ByZIndex)) continue;
            currentElements.OrderedAdd(item, x => x.ZIndex);
        }
    }

    private sealed record LayerSnapshot(Dictionary<int, TimelineLayer> ByZIndex, bool HasSolo);

    private static readonly LayerSnapshot s_emptyLayerSnapshot = new([], false);

    // Concurrent rebuilds after an invalidation are benign: each produces an
    // equivalent snapshot and the last write wins.
    private LayerSnapshot GetLayerSnapshot()
    {
        LayerSnapshot? snapshot = _layerSnapshot;
        if (snapshot is not null) return snapshot;

        if (Scene.Layers.Count == 0)
        {
            _layerSnapshot = s_emptyLayerSnapshot;
            return s_emptyLayerSnapshot;
        }

        var byZIndex = new Dictionary<int, TimelineLayer>(Scene.Layers.Count);
        bool hasSolo = false;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            if (byZIndex.TryAdd(layer.ZIndex, layer) && layer.IsSolo)
            {
                hasSolo = true;
            }
        }

        snapshot = new LayerSnapshot(byZIndex, hasSolo);
        _layerSnapshot = snapshot;
        return snapshot;
    }

    private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _layerSnapshot = null;

    private void OnSceneChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CleanupBatch cleanup = new();
        lock (_resourceCacheGate)
        {
            if (_isDisposed)
                return;

            _sceneChildrenGeneration++;
            int currentThreadId = Environment.CurrentManagedThreadId;
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach ((EngineObject obj, ObjectResourceState state) in _objectResourceStates)
                {
                    InvalidateObjectResources(obj, state, cleanup, currentThreadId);
                }
            }
            else if (e.OldItems != null)
            {
                var seen = new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);
                foreach (Element element in e.OldItems)
                {
                    foreach (EngineObject obj in element.Objects)
                    {
                        if (seen.Add(obj)
                            && _objectResourceStates.TryGetValue(obj, out ObjectResourceState? state))
                        {
                            InvalidateObjectResources(obj, state, cleanup, currentThreadId);
                        }
                    }
                }
            }
        }

        Exception? failure = ExecuteCleanupBatch(cleanup);
        ThrowFirstFailure(failure);
    }

    private void OnLayerAttached(TimelineLayer layer)
    {
        layer.PropertyChanged += OnLayerPropertyChanged;
        _layerSnapshot = null;
    }

    private void OnLayerDetached(TimelineLayer layer)
    {
        layer.PropertyChanged -= OnLayerPropertyChanged;
        _layerSnapshot = null;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimelineLayer.ZIndex) or nameof(TimelineLayer.IsSolo))
        {
            _layerSnapshot = null;
        }
    }

    // A layer without a TimelineLayer model cannot be soloed, so it is excluded
    // under solo mode. Mute is independent per target (audio vs video).
    private static bool ShouldSkipLayer(
        int zIndex,
        CompositionTarget target,
        bool hasSolo,
        Dictionary<int, TimelineLayer> layersByZIndex)
    {
        layersByZIndex.TryGetValue(zIndex, out TimelineLayer? layer);
        if (hasSolo && (layer is null || !layer.IsSolo)) return true;
        if (layer is null) return false;
        return target == CompositionTarget.Graphics ? layer.IsVideoMuted : layer.IsAudioMuted;
    }

    public void Dispose()
    {
        List<(EngineObject Object, ObjectResourceState State)> subscriptions = [];
        var cleanup = new CleanupBatch();
        bool removeSceneSubscriptions = false;
        lock (_resourceCacheGate)
        {
            if (_disposeCompleted)
            {
                return;
            }

            bool firstRequest = !_isDisposed;
            _isDisposed = true;
            int currentThreadId = Environment.CurrentManagedThreadId;
            foreach (var kvp in _objectResourceStates)
            {
                ObjectResourceState state = kvp.Value;
                if (firstRequest)
                {
                    state.Generation++;
                    state.IsDetached = true;
                }

                if (state.Subscription == SubscriptionState.Added)
                {
                    state.Subscription = SubscriptionState.Removing;
                    state.SubscriptionOwnerThreadId = currentThreadId;
                    subscriptions.Add((kvp.Key, state));
                }
                else if (state.Subscription == SubscriptionState.Adding)
                {
                    state.RemoveSubscriptionAfterAdd = true;
                }
            }

            foreach (var kvp in _frameResourceCache)
            {
                CollectIdleResourceForDispose(kvp.Value, cleanup, currentThreadId);
            }

            foreach (var kvp in _auxiliaryResourceCache)
            {
                CollectIdleResourceForDispose(kvp.Value, cleanup, currentThreadId);
            }

            if (!_sceneSubscriptionsRemovalClaimed)
            {
                _sceneSubscriptionsRemovalClaimed = true;
                removeSceneSubscriptions = true;
            }

            Monitor.PulseAll(_resourceCacheGate);
        }

        // Every event removal and Resource.Dispose is extension/user code from the cache's perspective.
        // Sweep all of it outside the gate and rethrow only the first failure after cleanup completes.
        Exception? failure = null;
        if (removeSceneSubscriptions)
        {
            TryCleanup(() => Scene.Children.CollectionChanged -= OnSceneChildrenCollectionChanged, ref failure);
            TryCleanup(() => Scene.Layers.CollectionChanged -= OnLayersCollectionChanged, ref failure);
            TryCleanup(() => Scene.Layers.Attached -= OnLayerAttached, ref failure);
            TryCleanup(() => Scene.Layers.Detached -= OnLayerDetached, ref failure);

            TimelineLayer[] layers = [];
            try
            {
                layers = [.. Scene.Layers];
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            foreach (TimelineLayer layer in layers)
            {
                TryCleanup(() => layer.PropertyChanged -= OnLayerPropertyChanged, ref failure);
            }
        }

        foreach ((EngineObject obj, ObjectResourceState state) in subscriptions)
        {
            try
            {
                obj.DetachedFromHierarchy -= state.Handler;
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
            finally
            {
                lock (_resourceCacheGate)
                {
                    state.Subscription = SubscriptionState.None;
                    state.SubscriptionOwnerThreadId = 0;
                    Monitor.PulseAll(_resourceCacheGate);
                }
            }
        }

        Exception? resourceFailure = ExecuteCleanupBatch(cleanup);
        failure ??= resourceFailure;

        lock (_resourceCacheGate)
        {
            if (!HasOutstandingCleanup())
            {
                _objectResourceStates.Clear();
                _frameResourceCache.Clear();
                _auxiliaryResourceCache.Clear();
                _resourceOwners.Clear();
                _disposeCompleted = true;
            }

            Monitor.PulseAll(_resourceCacheGate);
        }

        ThrowFirstFailure(failure);
    }

    private void CollectIdleResourceForDispose(
        ResourceSlot slot,
        CleanupBatch cleanup,
        int cleanupOwnerThreadId)
    {
        if (slot.IsBusy || slot.Resource == null)
        {
            return;
        }

        ClaimIdleSlotForCleanup(slot, cleanup, cleanupOwnerThreadId);
    }

    private bool HasOutstandingCleanup()
    {
        foreach (var kvp in _frameResourceCache)
        {
            if (kvp.Value.IsBusy || kvp.Value.Resource != null)
                return true;
        }

        foreach (var kvp in _auxiliaryResourceCache)
        {
            if (kvp.Value.IsBusy || kvp.Value.Resource != null)
                return true;
        }

        foreach (var kvp in _objectResourceStates)
        {
            if (kvp.Value.Subscription != SubscriptionState.None)
                return true;
        }

        return false;
    }
}
