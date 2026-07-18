using System.Runtime.ExceptionServices;
using Beutl.Composition;

namespace Beutl.Engine;

public static class ResourceReconciler
{
    /// <summary>
    /// Creates a one-shot transaction that reconciles any number of heterogeneous flow-backed resource lists.
    /// Requests are only planned and acquired when <see cref="FlowReconciliationTransaction.Commit(ref bool)"/>
    /// is called, so every consumed prefix participates in one transfer set and one retired-graph reservation.
    /// </summary>
    public static FlowReconciliationTransaction BeginFlowTransaction(
        CompositionContext context,
        IReadOnlyList<EngineObject.Resource>? flowRollbackSnapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new FlowReconciliationTransaction(context, flowRollbackSnapshot);
    }

    public sealed class FlowReconciliationTransaction
    {
        private readonly object _gate = new();
        private readonly CompositionContext _context;
        private readonly IReadOnlyList<EngineObject.Resource>? _flowRollbackSnapshot;
        private readonly List<IFlowReconciliationRequest> _requests = [];
        private readonly HashSet<object> _registeredFields = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _registeredVersions = new(ReferenceEqualityComparer.Instance);
        private bool _completed;

        internal FlowReconciliationTransaction(
            CompositionContext context,
            IReadOnlyList<EngineObject.Resource>? flowRollbackSnapshot)
        {
            _context = context;
            _flowRollbackSnapshot = flowRollbackSnapshot == null
                ? null
                : flowRollbackSnapshot.ToArray();
        }

        /// <summary>
        /// Adds one list reconciliation request without acquiring or retiring resources.
        /// </summary>
        public void Add<TItem, TResource>(
            IListProperty<TItem> property,
            IList<TResource> consumed,
            List<TResource> field,
            IList<int> versions)
            where TItem : EngineObject
            where TResource : EngineObject.Resource
        {
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(consumed);
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(versions);

            lock (_gate)
            {
                if (_completed)
                    throw new InvalidOperationException("The flow reconciliation transaction has already committed.");
                if (_registeredFields.Contains(field) || _registeredVersions.Contains(versions))
                {
                    throw new InvalidOperationException(
                        "A resource field or version list cannot be registered twice in one flow transaction.");
                }

                _registeredFields.Add(field);
                _registeredVersions.Add(versions);
                _requests.Add(new FlowReconciliationRequest<TItem, TResource>(
                    property,
                    consumed.ToArray(),
                    field,
                    versions));
            }
        }

        /// <summary>
        /// Plans every registered list, atomically reserves and retires the union of old ownership graphs, then
        /// commits every list and updates retained resources. The transaction cannot be reused after this call,
        /// including when reconciliation fails.
        /// </summary>
        public void Commit(ref bool changed)
        {
            IFlowReconciliationRequest[] requests;
            lock (_gate)
            {
                if (_completed)
                    throw new InvalidOperationException("The flow reconciliation transaction has already committed.");
                _completed = true;
                requests = _requests.ToArray();
            }

            ExecuteFlowTransaction(
                _context,
                requests,
                _flowRollbackSnapshot,
                ref changed);
        }
    }

    public static void ReconcileListFromFlow<TItem, TResource>(
        CompositionContext context, IListProperty<TItem> property,
        IList<TResource> consumed, List<TResource> field,
        IList<int> versions,
        IReadOnlyList<EngineObject.Resource>? flowRollbackSnapshot,
        ref bool changed)
        where TItem : EngineObject where TResource : EngineObject.Resource
    {
        FlowReconciliationTransaction transaction = BeginFlowTransaction(context, flowRollbackSnapshot);
        transaction.Add(property, consumed, field, versions);
        transaction.Commit(ref changed);
    }

    public static void ReconcileListsFromFlow<TItem1, TResource1, TItem2, TResource2>(
        CompositionContext context,
        IListProperty<TItem1> firstProperty,
        IList<TResource1> firstConsumed,
        List<TResource1> firstField,
        IList<int> firstVersions,
        IListProperty<TItem2> secondProperty,
        IList<TResource2> secondConsumed,
        List<TResource2> secondField,
        IList<int> secondVersions,
        IReadOnlyList<EngineObject.Resource>? flowRollbackSnapshot,
        ref bool changed)
        where TItem1 : EngineObject
        where TResource1 : EngineObject.Resource
        where TItem2 : EngineObject
        where TResource2 : EngineObject.Resource
    {
        FlowReconciliationTransaction transaction = BeginFlowTransaction(context, flowRollbackSnapshot);
        transaction.Add(firstProperty, firstConsumed, firstField, firstVersions);
        transaction.Add(secondProperty, secondConsumed, secondField, secondVersions);
        transaction.Commit(ref changed);
    }

    public static void ReconcileListFromProperty<TItem, TResource>(
        CompositionContext context, IListProperty<TItem> prop,
        int offsetIndex, IList<TResource> field, ref bool changed)
        where TItem : EngineObject where TResource : EngineObject.Resource
    {
        if ((uint)offsetIndex > (uint)field.Count)
            throw new ArgumentOutOfRangeException(nameof(offsetIndex));

        TItem[] owners = prop.ToArray();
        int previousCount = field.Count - offsetIndex;
        var previous = new TResource[previousCount];
        for (int i = 0; i < previousCount; i++)
            previous[i] = field[offsetIndex + i];

        var next = new List<TResource>(owners.Length);
        var acquired = new List<TResource>();
        var retired = new List<TResource>();
        bool structuralChange = previousCount != owners.Length;
        if (structuralChange)
            EnsureCanReplaceContents(field, offsetIndex + owners.Length, nameof(field));
        try
        {
            for (int i = 0; i < owners.Length; i++)
            {
                TItem child = owners[i];
                if (i < previousCount && ReferenceEquals(previous[i].GetOriginal(), child))
                {
                    next.Add(previous[i]);
                }
                else
                {
                    if (!structuralChange)
                        EnsureCanReplaceContents(field, offsetIndex + owners.Length, nameof(field));

                    TResource replacement = EngineObject.Resource.AcquireOwnedResource<TResource>(child, context);
                    acquired.Add(replacement);
                    next.Add(replacement);
                    structuralChange = true;
                    if (i < previousCount)
                        retired.Add(previous[i]);
                }
            }

            for (int i = owners.Length; i < previousCount; i++)
                retired.Add(previous[i]);
        }
        catch
        {
            DisposeUnpublishedResources(acquired);
            throw;
        }

        ExceptionDispatchInfo? cleanupFailure;
        try
        {
            cleanupFailure = EngineObject.Resource.RetireOwnedResourceGraphs(retired);
        }
        catch
        {
            DisposeUnpublishedResources(acquired);
            throw;
        }

        if (structuralChange)
        {
            ReplaceSuffix(field, offsetIndex, next);
            changed = true;
        }

        cleanupFailure?.Throw();

        for (int i = 0; i < owners.Length; i++)
        {
            TResource item = structuralChange ? next[i] : previous[i];
            if (i < previousCount && ReferenceEquals(item, previous[i]))
            {
                int oldVersion = item.Version;
                bool childUpdateOnly = false;
                item.Update(owners[i], context, ref childUpdateOnly);
                changed |= oldVersion != item.Version;
            }
        }
    }

    private static void ExecuteFlowTransaction(
        CompositionContext context,
        IReadOnlyList<IFlowReconciliationRequest> requests,
        IReadOnlyList<EngineObject.Resource>? flowRollbackSnapshot,
        ref bool changed)
    {
        var transferredResources = new HashSet<EngineObject.Resource>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < requests.Count; i++)
            requests[i].AddTransferredResources(transferredResources);

        var plans = new List<IListReconciliationPlan>(requests.Count);
        try
        {
            for (int i = 0; i < requests.Count; i++)
                plans.Add(requests[i].CreatePlan(context, transferredResources));
        }
        catch
        {
            for (int i = 0; i < plans.Count; i++)
                plans[i].DisposeAcquiredResources();
            RestoreFlow(context, flowRollbackSnapshot);
            throw;
        }

        var retired = new List<EngineObject.Resource>();
        for (int i = 0; i < plans.Count; i++)
            plans[i].AddRetiredResources(retired);

        ExceptionDispatchInfo? cleanupFailure;
        try
        {
            cleanupFailure = EngineObject.Resource.RetireOwnedResourceGraphs(retired);
        }
        catch
        {
            for (int i = 0; i < plans.Count; i++)
                plans[i].DisposeAcquiredResources();
            RestoreFlow(context, flowRollbackSnapshot);
            throw;
        }

        for (int i = 0; i < plans.Count; i++)
            plans[i].Commit(ref changed);
        cleanupFailure?.Throw();
        for (int i = 0; i < plans.Count; i++)
            plans[i].UpdateRetainedResources(ref changed);
    }

    private interface IFlowReconciliationRequest
    {
        void AddTransferredResources(HashSet<EngineObject.Resource> resources);

        IListReconciliationPlan CreatePlan(
            CompositionContext context,
            IReadOnlySet<EngineObject.Resource> transferredResources);
    }

    private interface IListReconciliationPlan
    {
        void AddRetiredResources(List<EngineObject.Resource> resources);

        void DisposeAcquiredResources();

        void Commit(ref bool changed);

        void UpdateRetainedResources(ref bool changed);
    }

    private sealed class FlowReconciliationRequest<TItem, TResource>(
        IListProperty<TItem> property,
        IList<TResource> consumed,
        List<TResource> field,
        IList<int> versions) : IFlowReconciliationRequest
        where TItem : EngineObject
        where TResource : EngineObject.Resource
    {
        public void AddTransferredResources(HashSet<EngineObject.Resource> resources)
        {
            ResourceReconciler.AddTransferredResources(consumed, resources);
        }

        public IListReconciliationPlan CreatePlan(
            CompositionContext context,
            IReadOnlySet<EngineObject.Resource> transferredResources)
        {
            return new ListReconciliationPlan<TItem, TResource>(
                context,
                property,
                consumed,
                field,
                versions,
                transferredResources);
        }
    }

    private sealed class ListReconciliationPlan<TItem, TResource> : IListReconciliationPlan
        where TItem : EngineObject
        where TResource : EngineObject.Resource
    {
        private readonly CompositionContext _context;
        private readonly TItem[] _owners;
        private readonly IList<TResource> _consumed;
        private readonly List<TResource> _field;
        private readonly IList<int> _versions;
        private readonly TResource[] _previousOwned;
        private readonly List<TResource> _nextOwned;
        private readonly List<TResource> _acquired = [];
        private readonly List<TResource> _retired = [];
        private readonly bool _ownedStructureChanged;
        private readonly bool _fieldStructureChanged;
        private readonly bool _changed;

        public ListReconciliationPlan(
            CompositionContext context,
            IListProperty<TItem> property,
            IList<TResource> consumed,
            List<TResource> field,
            IList<int> versions,
            IReadOnlySet<EngineObject.Resource> transferredResources)
        {
            _context = context;
            _owners = property.ToArray();
            _consumed = consumed;
            _field = field;
            _versions = versions;

            EnsureCanReplaceContents(versions, consumed.Count, nameof(versions));

            int previousPrefixCount = versions.Count;
            if (previousPrefixCount > field.Count)
            {
                throw new InvalidOperationException(
                    "The consumed-resource version prefix exceeds the resource list.");
            }

            int previousOwnedCount = field.Count - previousPrefixCount;
            _previousOwned = new TResource[previousOwnedCount];
            for (int i = 0; i < previousOwnedCount; i++)
                _previousOwned[i] = field[previousPrefixCount + i];

            bool prefixSequenceChanged = consumed.Count != previousPrefixCount;
            bool prefixVersionChanged = prefixSequenceChanged;
            int comparablePrefixCount = Math.Min(consumed.Count, previousPrefixCount);
            for (int i = 0; i < comparablePrefixCount; i++)
            {
                prefixSequenceChanged |= !ReferenceEquals(field[i], consumed[i]);
                prefixVersionChanged |= versions[i] != consumed[i].Version;
            }

            _nextOwned = new List<TResource>(_owners.Length);
            bool ownedStructureChanged = previousOwnedCount != _owners.Length;
            try
            {
                for (int i = 0; i < _owners.Length; i++)
                {
                    TItem child = _owners[i];
                    bool hasPrevious = i < previousOwnedCount;
                    bool previousTransferredToFlow = hasPrevious
                        && transferredResources.Contains(_previousOwned[i]);
                    if (hasPrevious
                        && !previousTransferredToFlow
                        && ReferenceEquals(_previousOwned[i].GetOriginal(), child))
                    {
                        _nextOwned.Add(_previousOwned[i]);
                    }
                    else
                    {
                        TResource replacement = EngineObject.Resource.AcquireOwnedResource<TResource>(child, context);
                        _acquired.Add(replacement);
                        _nextOwned.Add(replacement);
                        ownedStructureChanged = true;
                        if (hasPrevious && !previousTransferredToFlow)
                            _retired.Add(_previousOwned[i]);
                    }
                }

                for (int i = _owners.Length; i < previousOwnedCount; i++)
                {
                    if (!transferredResources.Contains(_previousOwned[i]))
                        _retired.Add(_previousOwned[i]);
                }
            }
            catch
            {
                DisposeAcquiredResources();
                throw;
            }

            _ownedStructureChanged = ownedStructureChanged;
            _fieldStructureChanged = prefixSequenceChanged || ownedStructureChanged;
            _changed = _fieldStructureChanged || prefixVersionChanged;
        }

        public void AddRetiredResources(List<EngineObject.Resource> resources)
        {
            for (int i = 0; i < _retired.Count; i++)
                resources.Add(_retired[i]);
        }

        public void DisposeAcquiredResources()
        {
            DisposeUnpublishedResources(_acquired);
            _acquired.Clear();
        }

        public void Commit(ref bool changed)
        {
            if (_fieldStructureChanged)
            {
                _field.Clear();
                for (int i = 0; i < _consumed.Count; i++)
                    _field.Add(_consumed[i]);
                for (int i = 0; i < _nextOwned.Count; i++)
                    _field.Add(_nextOwned[i]);
            }

            if (_versions.Count == _consumed.Count)
            {
                for (int i = 0; i < _consumed.Count; i++)
                    _versions[i] = _consumed[i].Version;
            }
            else
            {
                _versions.Clear();
                for (int i = 0; i < _consumed.Count; i++)
                    _versions.Add(_consumed[i].Version);
            }
            changed |= _changed;
        }

        public void UpdateRetainedResources(ref bool changed)
        {
            for (int i = 0; i < _owners.Length; i++)
            {
                TResource item = _ownedStructureChanged ? _nextOwned[i] : _previousOwned[i];
                if (i < _previousOwned.Length && ReferenceEquals(item, _previousOwned[i]))
                {
                    int oldVersion = item.Version;
                    bool childUpdateOnly = false;
                    item.Update(_owners[i], _context, ref childUpdateOnly);
                    changed |= oldVersion != item.Version;
                }
            }
        }
    }

    private static void AddTransferredResources<TResource>(
        IList<TResource> resources,
        HashSet<EngineObject.Resource> transferredResources)
        where TResource : EngineObject.Resource
    {
        for (int i = 0; i < resources.Count; i++)
            transferredResources.Add(resources[i]);
    }

    private static void EnsureCanReplaceContents<T>(IList<T> list, int targetCount, string parameterName)
    {
        if (list is System.Collections.IList nonGeneric)
        {
            if (nonGeneric.IsReadOnly || (nonGeneric.IsFixedSize && list.Count != targetCount))
            {
                throw new NotSupportedException(
                    $"{parameterName} does not support replacing its contents with {targetCount} items.");
            }

            return;
        }

        if (list.IsReadOnly)
        {
            throw new NotSupportedException(
                $"{parameterName} does not support replacing its contents with {targetCount} items.");
        }
    }

    private static void ReplaceSuffix<T>(IList<T> list, int offset, IReadOnlyList<T> replacement)
    {
        int targetCount = offset + replacement.Count;
        if (list.Count == targetCount)
        {
            for (int i = 0; i < replacement.Count; i++)
                list[offset + i] = replacement[i];
            return;
        }

        while (list.Count > offset)
            list.RemoveAt(list.Count - 1);
        for (int i = 0; i < replacement.Count; i++)
            list.Add(replacement[i]);
    }

    private static void DisposeUnpublishedResources<TResource>(IReadOnlyList<TResource> resources)
        where TResource : EngineObject.Resource
    {
        foreach (TResource resource in resources)
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Preserve the acquisition or reservation failure that prevented publication.
            }
        }
    }

    private static void RestoreFlow(
        CompositionContext context,
        IReadOnlyList<EngineObject.Resource>? snapshot)
    {
        if (snapshot == null)
            return;

        if (context.Flow == null)
            context.Flow = new List<EngineObject.Resource>(snapshot.Count);
        else
            context.Flow.Clear();

        for (int i = 0; i < snapshot.Count; i++)
            context.Flow.Add(snapshot[i]);
    }

    public static void ReconcileResource<TObject, TResource>(
        CompositionContext context, TObject value,
        ref TResource? field, ref bool changed)
        where TObject : EngineObject? where TResource : EngineObject.Resource
    {
        if (value is null)
        {
            if (field is not null)
            {
                ExceptionDispatchInfo? cleanupFailure = EngineObject.Resource.ClearOwnedResource(ref field);
                changed = true;
                cleanupFailure?.Throw();
            }
        }
        else
        {
            if (field is null)
            {
                field = EngineObject.Resource.AcquireOwnedResource<TResource>(value, context);
                changed = true;
            }
            else
            {
                if (field.GetOriginal() != value)
                {
                    TResource replacement = EngineObject.Resource.AcquireOwnedResource<TResource>(value, context);
                    ExceptionDispatchInfo? cleanupFailure = EngineObject.Resource.ReplaceOwnedResource(
                        ref field,
                        replacement);
                    changed = true;
                    cleanupFailure?.Throw();
                }
                else
                {
                    var oldVersion = field.Version;
                    var _ = false;
                    field.Update(value, context, ref _);
                    if (oldVersion != field.Version)
                    {
                        changed = true;
                    }
                }
            }
        }
    }

}
