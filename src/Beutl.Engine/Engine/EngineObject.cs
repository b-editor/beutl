using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Engine;

public sealed partial class FallbackEngineObject : EngineObject, IFallback;

[FallbackType(typeof(FallbackEngineObject))]
public class EngineObject : Hierarchical, INotifyEdited
{
    // これらのプロパティは描画時ではなく編集時に更新されるべき
    public static readonly CoreProperty<bool> IsTimeAnchorProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<TimeRange> TimeRangeProperty;
    private bool _isTimeAnchor;
    private bool _isEnabled = true;
    private int _zIndex;
    private TimeRange _timeRange;
    private IDisposable? _timeAnchorSubscription;
    private readonly List<IProperty> _properties = new();
    private List<IProperty>? _displayProperties;

    public event EventHandler? Edited;

    static EngineObject()
    {
        IsTimeAnchorProperty = ConfigureProperty<bool, EngineObject>(nameof(IsTimeAnchor))
            .Accessor(o => o.IsTimeAnchor, (o, v) => o.IsTimeAnchor = v)
            .DefaultValue(false)
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, EngineObject>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        ZIndexProperty = ConfigureProperty<int, EngineObject>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .Register();

        TimeRangeProperty = ConfigureProperty<TimeRange, EngineObject>(nameof(TimeRange))
            .Accessor(o => o.TimeRange, (o, v) => o.TimeRange = v)
            .Register();

        AffectsRender<EngineObject>(IsEnabledProperty, IsTimeAnchorProperty, ZIndexProperty, TimeRangeProperty);
    }

    public virtual IReadOnlyList<IProperty> Properties => _properties;

    [NotAutoSerialized]
    public bool IsTimeAnchor
    {
        get => _isTimeAnchor;
        set => SetAndRaise(IsTimeAnchorProperty, ref _isTimeAnchor, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    [NotAutoSerialized]
    public int ZIndex
    {
        get => _zIndex;
        set => SetAndRaise(ZIndexProperty, ref _zIndex, value);
    }

    [NotAutoSerialized]
    public TimeRange TimeRange
    {
        get => _timeRange;
        set => SetAndRaise(TimeRangeProperty, ref _timeRange, value);
    }

    public TimeSpan Start => TimeRange.Start;

    public TimeSpan Duration => TimeRange.Duration;

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs<bool> boolArgs)
        {
            if (boolArgs.Property.Id == IsTimeAnchorProperty.Id)
            {
                if (boolArgs.NewValue)
                {
                    RevokeTimeAnchorSubscription();
                }
                else
                {
                    SubscribeTimeAnchor();
                }
            }
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        if (IsTimeAnchor) return;

        SubscribeTimeAnchor();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        RevokeTimeAnchorSubscription();
    }

    private void RevokeTimeAnchorSubscription()
    {
        _timeAnchorSubscription?.Dispose();
        _timeAnchorSubscription = null;
    }

    private void SubscribeTimeAnchor()
    {
        _timeAnchorSubscription?.Dispose();

        var parent = this.FindHierarchicalParent<EngineObject>();
        if (parent == null) return;

        var d1 = parent.GetObservable(TimeRangeProperty)
            .Subscribe(t => TimeRange = t);

        var d2 = parent.GetObservable(ZIndexProperty)
            .Subscribe(z => ZIndex = z);

        _timeAnchorSubscription = Disposable.Create((d1, d2), t => t.DisposeAll());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        var start = context.GetValue<Optional<TimeSpan>>(nameof(TimeRange.Start));
        var duration = context.GetValue<Optional<TimeSpan>>(nameof(TimeRange.Duration));
        var zIndex = context.GetValue<Optional<int>>(nameof(ZIndex));
        if (start.HasValue && duration.HasValue)
            TimeRange = new TimeRange(start.Value, duration.Value);
        if (zIndex.HasValue)
            ZIndex = zIndex.Value;
        IsTimeAnchor = start.HasValue && duration.HasValue && zIndex.HasValue;

        Dictionary<string, IAnimation>? animations
            = context.GetValue<Dictionary<string, IAnimation>>("Animations");

        Dictionary<string, JsonNode>? expressions
            = context.GetValue<Dictionary<string, JsonNode>>("Expressions");

        foreach (IProperty property in _properties)
        {
            property.DeserializeValue(context);
            if (property.IsAnimatable && animations?.TryGetValue(property.Name, out IAnimation? animation) == true)
            {
                property.Animation = animation;
            }

            if (expressions?.TryGetValue(property.Name, out JsonNode? expressionNode) == true)
            {
                property.DeserializeExpression(expressionNode);
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        if (IsTimeAnchor)
        {
            context.SetValue(nameof(TimeRange.Start), TimeRange.Start);
            context.SetValue(nameof(TimeRange.Duration), TimeRange.Duration);
            context.SetValue(nameof(ZIndex), ZIndex);
        }

        Dictionary<string, IAnimation> animations = _properties
            .Where(p => p is { IsAnimatable: true, Animation: not null })
            .ToDictionary(p => p.Name, p => p.Animation!);

        context.SetValue("Animations", animations);

        Dictionary<string, JsonNode> expressions = _properties
            .Select(p => (Name: p.Name, Node: p.SerializeExpression()))
            .Where(p => p.Node is not null)
            .ToDictionary(p => p.Name, p => p.Node!);

        context.SetValue("Expressions", expressions);

        foreach (IProperty property in _properties)
        {
            property.SerializeValue(context);
        }
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : EngineObject
    {
        foreach (CoreProperty item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseEdited();

                    if (e.OldValue is INotifyEdited oldAffectsRender)
                    {
                        oldAffectsRender.Edited -= s.OnPropertyEdited;
                    }

                    if (e.NewValue is INotifyEdited newAffectsRender)
                    {
                        newAffectsRender.Edited += s.OnPropertyEdited;
                    }
                }
            });
        }
    }

    protected virtual IEnumerable<IProperty> ScanPropertiesCore<T>() where T : EngineObject
    {
        var type = typeof(T);
        var propertyInfos = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        for (int index = 0; index < propertyInfos.Length; index++)
        {
            PropertyInfo propertyInfo = propertyInfos[index];
            if (!typeof(IProperty).IsAssignableFrom(propertyInfo.PropertyType)) continue;

            var func = PropertyReflectionCache.GetOrCreateAccessor(type, propertyInfo);
            var property = func(this);
            if (property == null) continue;

            var attrs = PropertyReflectionCache.GetOrCreateAttributes(type, propertyInfo.Name,
                () => [.. propertyInfo.GetCustomAttributes()]);
            var validator = PropertyReflectionCache.GetOrCreateValidator(type, propertyInfo.Name,
                () => property.CreateValidator(attrs));

            property.SetAttributes(propertyInfo.Name, attrs);
            property.SetValidator(validator);
            property.SetOwnerObject(this);
            yield return property;
        }
    }

    protected void ScanProperties<T>() where T : EngineObject
    {
        int index = 0;
        foreach (IProperty property in ScanPropertiesCore<T>())
        {
            RegisterProperty(property, index++);
        }
    }

    protected void RegisterProperty(IProperty property)
    {
        if (!_properties.Contains(property))
        {
            _properties.Add(property);
            property.Edited += OnPropertyEdited;
        }

        if (_displayProperties?.Contains(property) == false)
        {
            _displayProperties.Add(property);
        }
    }

    protected void RegisterProperty(IProperty property, int index)
    {
        if (!_properties.Contains(property))
        {
            _properties.Insert(Math.Min(index, _properties.Count), property);
            property.Edited += OnPropertyEdited;
        }

        if (_displayProperties?.Contains(property) == false)
        {
            _displayProperties.Insert(Math.Min(index, _displayProperties.Count), property);
        }
    }

    public IReadOnlyList<IProperty> GetDisplayProperties() => _displayProperties ?? _properties;

    protected void HideProperty(IProperty property)
    {
        EnsureDisplayProperties();
        _displayProperties.Remove(property);
    }

    protected void HideProperties(params IProperty[] properties)
    {
        EnsureDisplayProperties();
        _displayProperties.RemoveAll(p => properties.Contains(p));
    }

    protected void MoveProperty(IProperty property, int index)
    {
        EnsureDisplayProperties();
        _displayProperties.Remove(property);
        _displayProperties.Insert(Math.Min(index, _displayProperties.Count), property);
    }

    protected void MovePropertyBefore(IProperty property, IProperty target)
    {
        EnsureDisplayProperties();
        _displayProperties.Remove(property);
        int targetIndex = _displayProperties.IndexOf(target);
        if (targetIndex >= 0)
            _displayProperties.Insert(targetIndex, property);
        else
            _displayProperties.Add(property);
    }

    protected void MovePropertyAfter(IProperty property, IProperty target)
    {
        EnsureDisplayProperties();
        _displayProperties.Remove(property);
        int targetIndex = _displayProperties.IndexOf(target);
        if (targetIndex >= 0)
            _displayProperties.Insert(targetIndex + 1, property);
        else
            _displayProperties.Add(property);
    }

    protected void ReorderProperties(params IProperty[] ordered)
    {
        EnsureDisplayProperties();
        _displayProperties = [.. ordered, .. _displayProperties.Except(ordered)];
    }

    [MemberNotNull(nameof(_displayProperties))]
    private void EnsureDisplayProperties()
    {
        _displayProperties ??= [.. _properties];
    }

    private void OnPropertyEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    protected void RaiseEdited()
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }

    public virtual CompositionTarget GetCompositionTarget()
    {
        return CompositionTarget.Unknown;
    }

    public virtual Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        try
        {
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }
        catch
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Resource acquisition failed. Preserve the Update exception as the operation failure while still
                // attempting to release everything acquired by the partially initialized resource.
            }

            throw;
        }
    }

    public class Resource : IDisposable
    {
        private const int ActiveDisposeState = 0;
        private const int DisposingState = 1;
        private const int DisposedState = 2;

        private enum ReservedCleanupState
        {
            Reserved,
            Executing,
            Completed,
        }

        /// <summary>
        /// Coordinates the generated portions of one resource ownership graph cleanup. This type is infrastructure
        /// for source-generated resource code and manual implementations of its generated lifecycle partial hooks.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected sealed class GeneratedResourceCleanupContext
        {
            private readonly Dictionary<Resource, ReservedCleanupState> _resources
                = new(ReferenceEqualityComparer.Instance);
            private readonly List<Resource> _reservationOrder = [];
            private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
            private ExceptionDispatchInfo? _firstFailure;

            internal ExceptionDispatchInfo? FirstFailure => _firstFailure;

            /// <summary>
            /// Reserves an owned resource and its generated ownership graph before cleanup starts.
            /// </summary>
            public void Reserve(Resource? resource)
            {
                if (resource == null || _resources.ContainsKey(resource))
                    return;

                lock (resource._resourceLifecycleGate)
                {
                    if (resource._disposeState == DisposedState)
                        return;

                    if (resource._disposeState != ActiveDisposeState
                        || resource._resourceOperationDepth != 0)
                    {
                        throw new InvalidOperationException(
                            "An owned resource cannot be disposed while another cleanup, update, or node-port bind operation is in progress.");
                    }

                    _resources.Add(resource, ReservedCleanupState.Reserved);
                    try
                    {
                        _reservationOrder.Add(resource);
                    }
                    catch
                    {
                        _resources.Remove(resource);
                        throw;
                    }

                    resource._disposeState = DisposingState;
                    resource._disposeOwnerThreadId = _ownerThreadId;
                }

                resource.PrepareGeneratedResourceCleanupCore(disposing: true, this);
            }

            /// <summary>
            /// Disposes a resource reserved during the pre-cleanup ownership walk.
            /// </summary>
            public void DisposeOwned(Resource? resource)
            {
                if (resource == null || resource.IsDisposed)
                    return;

                if (!_resources.TryGetValue(resource, out ReservedCleanupState state))
                {
                    Capture(new InvalidOperationException(
                        "Generated cleanup attempted to dispose a resource that was not reserved."));
                    return;
                }

                if (state != ReservedCleanupState.Reserved)
                    return;

                _resources[resource] = ReservedCleanupState.Executing;
                resource.ExecuteReservedCleanup(disposing: true, this);
                _resources[resource] = ReservedCleanupState.Completed;
            }

            /// <summary>
            /// Disposes a non-resource owned value while retaining the first cleanup failure.
            /// </summary>
            public void DisposeOwned(IDisposable? disposable)
            {
                if (disposable is Resource resource)
                {
                    DisposeOwned(resource);
                    return;
                }

                if (disposable == null)
                    return;

                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    Capture(ex);
                }
            }

            /// <summary>
            /// Retains the exact first cleanup failure and its original dispatch information.
            /// </summary>
            public void Capture(Exception exception)
            {
                _firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }

            internal void ReserveClaimedRoot(Resource resource, bool disposing)
            {
                _resources.Add(resource, ReservedCleanupState.Reserved);
                try
                {
                    _reservationOrder.Add(resource);
                }
                catch
                {
                    _resources.Remove(resource);
                    throw;
                }

                resource.PrepareGeneratedResourceCleanupCore(disposing, this);
            }

            internal void RollbackReservations()
            {
                for (int i = _reservationOrder.Count - 1; i >= 0; i--)
                {
                    Resource resource = _reservationOrder[i];
                    try
                    {
                        resource.RollbackGeneratedResourceCleanupCore();
                    }
                    catch
                    {
                        // Rollback only resets generated preparation state. A malformed out-of-tree override must
                        // not strand the rest of the ownership graph in the disposing state.
                    }

                    lock (resource._resourceLifecycleGate)
                    {
                        if (resource._disposeState == DisposingState
                            && resource._disposeOwnerThreadId == _ownerThreadId)
                        {
                            resource._disposeOwnerThreadId = 0;
                            resource._disposeState = ActiveDisposeState;
                            Monitor.PulseAll(resource._resourceLifecycleGate);
                        }
                    }
                }
            }

            internal void DisposeAllReserved(Resource root, bool disposing)
            {
                DisposeReserved(root, disposing);
                for (int i = 0; i < _reservationOrder.Count; i++)
                {
                    DisposeReserved(_reservationOrder[i], disposing);
                }
            }

            private void DisposeReserved(Resource resource, bool disposing)
            {
                if (!_resources.TryGetValue(resource, out ReservedCleanupState state)
                    || state != ReservedCleanupState.Reserved)
                {
                    return;
                }

                _resources[resource] = ReservedCleanupState.Executing;
                resource.ExecuteReservedCleanup(disposing, this);
                _resources[resource] = ReservedCleanupState.Completed;
            }
        }

        /// <summary>
        /// An allocation-free, self-bound lease used by source-generated update and node-port binding code.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected ref struct GeneratedResourceOperationLease
        {
            private Resource? _owner;

            internal GeneratedResourceOperationLease(Resource owner)
            {
                _owner = owner;
            }

            /// <summary>
            /// Ends the generated operation represented by this lease.
            /// </summary>
            public void Dispose()
            {
                Resource? owner = _owner;
                if (owner == null)
                    return;

                _owner = null;
                owner.EndGeneratedResourceOperation();
            }
        }

        private ref struct StateProjectionOperationLease
        {
            private Resource? _owner;
            private readonly bool _previousRejectsGeneratedNesting;

            internal StateProjectionOperationLease(Resource owner, bool previousRejectsGeneratedNesting)
            {
                _owner = owner;
                _previousRejectsGeneratedNesting = previousRejectsGeneratedNesting;
            }

            public void Dispose()
            {
                Resource? owner = _owner;
                if (owner == null)
                    return;

                _owner = null;
                owner.EndStateProjectionOperation(_previousRejectsGeneratedNesting);
            }
        }

        ~Resource()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            lock (_resourceLifecycleGate)
            {
                if (_disposeState != ActiveDisposeState || _resourceOperationDepth != 0)
                    return;

                _disposeState = DisposingState;
                _disposeOwnerThreadId = currentThreadId;
            }

            try
            {
                var context = new GeneratedResourceCleanupContext();
                try
                {
                    context.ReserveClaimedRoot(this, disposing: false);
                }
                catch
                {
                    context.RollbackReservations();
                    return;
                }

                context.DisposeAllReserved(this, disposing: false);
            }
            catch
            {
                // Finalizers must never allow cleanup failures to escape onto the finalizer thread.
            }
            finally
            {
                lock (_resourceLifecycleGate)
                {
                    _disposeOwnerThreadId = 0;
                    _disposeState = DisposedState;
                    Monitor.PulseAll(_resourceLifecycleGate);
                }
            }
        }

        private EngineObject? _original;
        private readonly object _resourceLifecycleGate = new();
        private int _disposeState;
        private int _disposeOwnerThreadId;
        private int _resourceOperationDepth;
        private int _resourceOperationOwnerThreadId;
        private bool _resourceOperationRejectsGeneratedNesting;

        public int Version { get; set; }

        public bool IsEnabled { get; set; }

        /// <summary>
        /// Gets whether resource cleanup has finished.
        /// </summary>
        /// <remarks>
        /// This property remains <see langword="false"/> while <see cref="Dispose()"/> is running so existing
        /// overrides that guard cleanup with this property continue to execute. It becomes <see langword="true"/>
        /// after cleanup finishes, including when cleanup reports an exception, because partially released owned
        /// resources cannot be retried safely.
        /// </remarks>
        public bool IsDisposed => Volatile.Read(ref _disposeState) == DisposedState;

        /// <summary>
        /// Gets the source object supplied to the most recent update.
        /// </summary>
        /// <remarks>
        /// A directly constructed resource has no original until its first update begins. Cleanup hooks that support
        /// direct construction must not depend on this value unless they know an update has run.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The resource was directly constructed and has not started an update yet.
        /// </exception>
        public EngineObject GetOriginal()
        {
            return _original ?? throw new InvalidOperationException(
                "The resource has no original EngineObject until its first update begins.");
        }

        public virtual void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            using GeneratedResourceOperationLease operation = BeginGeneratedResourceOperation(obj);
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposeState) != ActiveDisposeState,
                this);
            if (IsEnabled != obj.IsEnabled)
            {
                IsEnabled = obj.IsEnabled;
                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }

        protected void CompareAndUpdate<TValue>(CompositionContext context, IProperty<TValue> prop, ref TValue field,
            ref bool updateOnly)
        {
            TValue newValue = context.Get(prop);
            TValue oldValue = field;
            field = newValue;
            if (updateOnly)
            {
                return;
            }

            if (!EqualityComparer<TValue>.Default.Equals(newValue, oldValue))
            {
                Version++;
                updateOnly = true;
            }
        }

        protected void CompareAndUpdateList<TItem, TResource>(CompositionContext context, IList<TItem> prop,
            ref List<TResource> field, ref bool updateOnly) where TItem : EngineObject where TResource : Resource
        {
            TItem[] owners = prop.ToArray();
            List<TResource> previous = field;
            var next = new List<TResource>(owners.Length);
            var acquired = new List<TResource>();
            var retired = new List<TResource>();
            bool structuralChange = previous.Count != owners.Length;

            try
            {
                for (int i = 0; i < owners.Length; i++)
                {
                    TItem child = owners[i];
                    if (i < previous.Count && ReferenceEquals(previous[i].GetOriginal(), child))
                    {
                        next.Add(previous[i]);
                    }
                    else
                    {
                        TResource replacement = AcquireOwnedResource<TResource>(child, context);
                        acquired.Add(replacement);
                        next.Add(replacement);
                        structuralChange = true;
                        if (i < previous.Count)
                            retired.Add(previous[i]);
                    }
                }

                for (int i = owners.Length; i < previous.Count; i++)
                    retired.Add(previous[i]);
            }
            catch
            {
                DisposeUnpublishedOwnedResources(acquired);
                throw;
            }

            ExceptionDispatchInfo? cleanupFailure;
            try
            {
                cleanupFailure = RetireOwnedResourceGraphs(retired);
            }
            catch
            {
                DisposeUnpublishedOwnedResources(acquired);
                throw;
            }

            if (structuralChange)
            {
                field = next;
                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
                }
            }

            cleanupFailure?.Throw();

            for (int i = 0; i < owners.Length; i++)
            {
                TResource item = structuralChange ? next[i] : previous[i];
                if (i < previous.Count && ReferenceEquals(item, previous[i]))
                {
                    int oldVersion = item.Version;
                    bool childUpdateOnly = false;
                    item.Update(owners[i], context, ref childUpdateOnly);
                    if (!updateOnly && oldVersion != item.Version)
                    {
                        Version++;
                        updateOnly = true;
                    }
                }
            }
        }

        protected void CompareAndUpdateObject<TObject, TResource>(CompositionContext context, IProperty<TObject> prop,
            ref TResource? field, ref bool updateOnly) where TObject : EngineObject? where TResource : Resource
        {
            var value = context.Get(prop);
            if (value is null)
            {
                if (field is not null)
                {
                    ExceptionDispatchInfo? cleanupFailure = ClearOwnedResource(ref field);
                    if (!updateOnly)
                    {
                        Version++;
                        updateOnly = true;
                    }

                    cleanupFailure?.Throw();
                }
            }
            else
            {
                if (field is null)
                {
                    field = AcquireOwnedResource<TResource>(value, context);
                    if (!updateOnly)
                    {
                        Version++;
                        updateOnly = true;
                    }
                }
                else
                {
                    if (field.GetOriginal() != value)
                    {
                        TResource replacement = AcquireOwnedResource<TResource>(value, context);
                        ExceptionDispatchInfo? cleanupFailure = ReplaceOwnedResource(ref field, replacement);
                        Version++;
                        updateOnly = true;
                        cleanupFailure?.Throw();
                    }
                    else
                    {
                        var oldVersion = field.Version;
                        var _ = false;
                        field.Update(value, context, ref _);
                        if (!updateOnly && oldVersion != field.Version)
                        {
                            Version++;
                            updateOnly = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Replaces one owned resource after atomically reserving and cleaning its previous ownership graph.
        /// Reservation failure leaves <paramref name="field"/> unchanged and disposes the unpublished replacement.
        /// A cleanup failure publishes the replacement and is returned with its original dispatch information.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected internal static ExceptionDispatchInfo? ReplaceOwnedResource<TResource>(
            ref TResource? field,
            TResource replacement)
            where TResource : Resource
        {
            ArgumentNullException.ThrowIfNull(replacement);
            TResource? previous = field;
            if (previous == null)
            {
                field = replacement;
                return null;
            }

            if (ReferenceEquals(previous, replacement))
                return null;

            GeneratedResourceCleanupContext context = ReserveOwnedResourceForMutation(previous, replacement);
            context.DisposeAllReserved(previous, disposing: true);
            field = replacement;
            return context.FirstFailure;
        }

        /// <summary>
        /// Clears one owned resource after reserving and cleaning its complete ownership graph.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected internal static ExceptionDispatchInfo? ClearOwnedResource<TResource>(ref TResource? field)
            where TResource : Resource
        {
            TResource? previous = field;
            if (previous == null)
                return null;

            GeneratedResourceCleanupContext context = ReserveOwnedResourceForMutation(previous, null);
            context.DisposeAllReserved(previous, disposing: true);
            field = null;
            return context.FirstFailure;
        }

        /// <summary>
        /// Atomically reserves and cleans every supplied owned resource graph without mutating the caller's owner
        /// collection. Reservation failure rolls back the complete batch; cleanup failure still sweeps every graph
        /// and is returned with its original dispatch information.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected internal static ExceptionDispatchInfo? RetireOwnedResourceGraphs<TResource>(
            IReadOnlyList<TResource> roots)
            where TResource : Resource
        {
            ArgumentNullException.ThrowIfNull(roots);
            if (roots.Count == 0)
                return null;

            var context = new GeneratedResourceCleanupContext();
            try
            {
                foreach (TResource resource in roots)
                {
                    ArgumentNullException.ThrowIfNull(resource);
                    context.Reserve(resource);
                }
            }
            catch
            {
                context.RollbackReservations();
                throw;
            }

            context.DisposeAllReserved(roots[0], disposing: true);
            return context.FirstFailure;
        }

        /// <summary>
        /// Acquires an owned resource of the required type and rolls the new resource back when a custom
        /// <see cref="EngineObject.ToResource(CompositionContext)"/> implementation returns an incompatible type.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected internal static TResource AcquireOwnedResource<TResource>(
            EngineObject owner,
            CompositionContext context)
            where TResource : Resource
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(context);
            Resource acquired = owner.ToResource(context);
            if (acquired is TResource typed)
                return typed;

            try
            {
                acquired?.Dispose();
            }
            catch
            {
                // Preserve the incompatible resource contract as the acquisition failure.
            }

            string actualType = acquired?.GetType().FullName ?? "null";
            throw new InvalidCastException(
                $"{owner.GetType().FullName}.ToResource returned {actualType}, but {typeof(TResource).FullName} was required.");
        }

        private static void DisposeUnpublishedOwnedResources<TResource>(IReadOnlyList<TResource> resources)
            where TResource : Resource
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

        private static GeneratedResourceCleanupContext ReserveOwnedResourceForMutation(
            Resource previous,
            Resource? unpublishedReplacement)
        {
            GeneratedResourceCleanupContext? context = null;
            try
            {
                context = new GeneratedResourceCleanupContext();
                context.Reserve(previous);
                return context;
            }
            catch
            {
                try
                {
                    context?.RollbackReservations();
                }
                catch
                {
                    // Preserve the reservation failure while still attempting to release the unpublished owner.
                }

                try
                {
                    unpublishedReplacement?.Dispose();
                }
                catch
                {
                    // The reservation failure is the operation failure. Replacement cleanup remains best-effort.
                }

                throw;
            }
        }

        /// <summary>
        /// Disposes every supplied non-<see cref="Resource"/> owned value in order, retaining the exact first cleanup exception in
        /// <paramref name="firstFailure"/> while continuing the full sweep. A null array and null elements are
        /// accepted and ignored. The caller retains ownership of the params array itself: this method neither
        /// disposes, clears, nor otherwise mutates it.
        /// </summary>
        /// <param name="firstFailure">
        /// The first cleanup exception already observed, or <see langword="null"/>. An existing exception is never
        /// replaced; otherwise the exact first exception instance thrown by a resource is stored.
        /// </param>
        /// <param name="resources">
        /// The owned non-<see cref="Resource"/> values to dispose. The array itself is not an owned value.
        /// <see cref="Resource"/> ownership graphs must instead be reserved through
        /// <see cref="GeneratedResourceCleanupContext.Reserve(Resource?)"/> before any owner is detached.
        /// </param>
        /// <exception cref="ArgumentException">
        /// A supplied value is an <see cref="Resource"/>. The complete ownership graph must be reserved atomically;
        /// disposing one resource through this best-effort helper could lose retryable ownership.
        /// </exception>
        protected static void DisposeOwnedResources(
            ref Exception? firstFailure,
            params IDisposable?[]? resources)
        {
            if (resources == null)
                return;

            foreach (IDisposable? resource in resources)
            {
                if (resource is Resource)
                {
                    throw new ArgumentException(
                        $"{nameof(DisposeOwnedResources)} cannot dispose {nameof(EngineObject)}.{nameof(Resource)} instances. "
                        + "Reserve and retire the complete owned resource graph before detaching it.",
                        nameof(resources));
                }
            }

            foreach (IDisposable? resource in resources)
            {
                if (resource == null)
                    continue;

                try
                {
                    resource.Dispose();
                }
                catch (Exception ex)
                {
                    firstFailure ??= ex;
                }
            }
        }

        /// <summary>
        /// Rethrows the exact first cleanup exception, preserving its original throw information, or returns when
        /// <paramref name="firstFailure"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="firstFailure">The first cleanup exception retained by a full cleanup sweep.</param>
        protected static void ThrowIfCleanupFailed(Exception? firstFailure)
        {
            if (firstFailure != null)
            {
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
            }
        }

        /// <summary>
        /// Starts one generated-resource update layer and returns its self-bound operation lease. Source-generated
        /// resource code uses this method; custom resource implementations should not call it directly.
        /// </summary>
        /// <remarks>
        /// Generated update layers may nest on one thread. A different thread cannot enter concurrently, and disposal
        /// from a different thread fails while an operation is active so a callback waiting for that disposal cannot
        /// deadlock its own update.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected GeneratedResourceOperationLease BeginGeneratedResourceOperation(EngineObject? original = null)
        {
            lock (_resourceLifecycleGate)
            {
                ObjectDisposedException.ThrowIf(
                    _disposeState != ActiveDisposeState,
                    this);

                int currentThreadId = Environment.CurrentManagedThreadId;
                if (_resourceOperationDepth != 0
                    && _resourceOperationOwnerThreadId != currentThreadId)
                {
                    throw new InvalidOperationException(
                        "A resource update or node-port bind operation cannot run concurrently on multiple threads.");
                }

                if (_resourceOperationDepth != 0 && _resourceOperationRejectsGeneratedNesting)
                {
                    throw new InvalidOperationException(
                        "A generated resource operation cannot re-enter a state projection callback.");
                }

                if (original != null)
                    _original = original;

                _resourceOperationOwnerThreadId = currentThreadId;
                _resourceOperationDepth++;
                return new GeneratedResourceOperationLease(this);
            }
        }

        /// <summary>
        /// Starts one exclusive operation implemented by a handwritten resource and rejects concurrent or reentrant
        /// entry. Generated base update layers invoked inside that operation may still use
        /// <see cref="BeginGeneratedResourceOperation(EngineObject)"/> to nest on the owning thread.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected GeneratedResourceOperationLease BeginExclusiveResourceOperation(EngineObject? original = null)
        {
            lock (_resourceLifecycleGate)
            {
                ObjectDisposedException.ThrowIf(
                    _disposeState != ActiveDisposeState,
                    this);

                if (_resourceOperationDepth != 0)
                {
                    throw new InvalidOperationException(
                        "A handwritten resource operation cannot run concurrently or re-enter the same resource.");
                }

                if (original != null)
                    _original = original;

                _resourceOperationOwnerThreadId = Environment.CurrentManagedThreadId;
                _resourceOperationDepth = 1;
                return new GeneratedResourceOperationLease(this);
            }
        }

        private StateProjectionOperationLease BeginStateProjectionOperation()
        {
            lock (_resourceLifecycleGate)
            {
                int currentThreadId = Environment.CurrentManagedThreadId;
                ObjectDisposedException.ThrowIf(_disposeState == DisposedState, this);
                if (_disposeState == DisposingState && _disposeOwnerThreadId != currentThreadId)
                {
                    throw new InvalidOperationException(
                        "A resource state projection cannot run while cleanup is owned by another thread.");
                }

                if (_resourceOperationDepth != 0
                    && _resourceOperationOwnerThreadId != currentThreadId)
                {
                    throw new InvalidOperationException(
                        "A resource state projection cannot run concurrently with another resource operation.");
                }

                if (_resourceOperationRejectsGeneratedNesting)
                {
                    throw new InvalidOperationException(
                        "A resource state projection cannot re-enter the same resource.");
                }

                bool previousRejectsGeneratedNesting = _resourceOperationRejectsGeneratedNesting;
                _resourceOperationOwnerThreadId = currentThreadId;
                _resourceOperationDepth++;
                _resourceOperationRejectsGeneratedNesting = true;
                return new StateProjectionOperationLease(this, previousRejectsGeneratedNesting);
            }
        }

        /// <summary>
        /// Validates source-generated property access against the graph-wide operation and cleanup state.
        /// Access from the thread currently running an update or cleanup hook remains valid.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected void ValidateGeneratedResourceAccess()
        {
            lock (_resourceLifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposeState == DisposedState, this);

                int currentThreadId = Environment.CurrentManagedThreadId;
                if (_disposeState == DisposingState && _disposeOwnerThreadId != currentThreadId)
                {
                    throw new InvalidOperationException(
                        "A generated resource property cannot be accessed while cleanup is running on another thread.");
                }

                if (_resourceOperationDepth != 0 && _resourceOperationOwnerThreadId != currentThreadId)
                {
                    throw new InvalidOperationException(
                        "A generated resource property cannot be accessed while an update or node-port bind operation is running on another thread.");
                }
            }
        }

        /// <summary>
        /// Reads one handwritten resource field while holding the lifecycle gate, so validation and the read cannot
        /// race an update or cleanup transition. Source-generated resources use their per-layer gate instead.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected T ReadGeneratedResourceState<T>(ref T state)
        {
            lock (_resourceLifecycleGate)
            {
                ValidateGeneratedResourceAccess();
                return state;
            }
        }

        /// <summary>
        /// Projects one handwritten resource field while holding the lifecycle gate. Use this overload when reading
        /// through an owned holder must be atomic with lifecycle validation as well as with the field read itself.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected TResult ReadGeneratedResourceState<TState, TResult>(
            ref TState state,
            Func<TState, TResult> selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            using StateProjectionOperationLease operation = BeginStateProjectionOperation();
            TState snapshot;
            lock (_resourceLifecycleGate)
            {
                ValidateGeneratedResourceAccess();
                snapshot = state;
            }

            return selector(snapshot);
        }

        /// <summary>
        /// Writes one handwritten resource value while holding the lifecycle gate used by update and cleanup.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected void WriteGeneratedResourceState<T>(ref T state, T value)
        {
            lock (_resourceLifecycleGate)
            {
                ValidateGeneratedResourceAccess();
                state = value;
            }
        }

        private void EndGeneratedResourceOperation()
        {
            lock (_resourceLifecycleGate)
            {
                if (_resourceOperationDepth <= 0
                    || _resourceOperationOwnerThreadId != Environment.CurrentManagedThreadId)
                {
                    throw new InvalidOperationException("The generated resource operation is not owned by this thread.");
                }

                _resourceOperationDepth--;
                if (_resourceOperationDepth == 0)
                {
                    _resourceOperationOwnerThreadId = 0;
                    _resourceOperationRejectsGeneratedNesting = false;
                    Monitor.PulseAll(_resourceLifecycleGate);
                }
            }
        }

        private void EndStateProjectionOperation(bool previousRejectsGeneratedNesting)
        {
            lock (_resourceLifecycleGate)
            {
                if (_resourceOperationDepth <= 0
                    || _resourceOperationOwnerThreadId != Environment.CurrentManagedThreadId
                    || !_resourceOperationRejectsGeneratedNesting)
                {
                    throw new InvalidOperationException("The resource state projection is not owned by this thread.");
                }

                _resourceOperationDepth--;
                _resourceOperationRejectsGeneratedNesting = previousRejectsGeneratedNesting;
                if (_resourceOperationDepth == 0)
                {
                    _resourceOperationOwnerThreadId = 0;
                    Monitor.PulseAll(_resourceLifecycleGate);
                }
            }
        }

        /// <summary>
        /// Reserves the resources owned by one generated inheritance chain before cleanup starts.
        /// Source-generated overrides and manually implemented resources that suppress generation must invoke the
        /// base implementation after preparing their own layer.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
        }

        /// <summary>
        /// Rolls back generated cleanup preparation after ownership-graph reservation fails.
        /// Source-generated overrides and manually implemented resources that suppress generation must invoke the
        /// base implementation before rolling back their own layer, reversing preparation order.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void RollbackGeneratedResourceCleanupCore()
        {
        }

        /// <summary>
        /// Cleans the resources owned by one generated inheritance chain after the complete graph is reserved.
        /// Source-generated overrides and manually implemented resources that suppress generation must invoke the
        /// base implementation from a <see langword="finally"/> block so every inherited layer runs.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void CleanupGeneratedResourceCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
        }

        private void ExecuteReservedCleanup(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            try
            {
                try
                {
                    Dispose(disposing);
                }
                catch (Exception ex)
                {
                    context.Capture(ex);
                }

                try
                {
                    CleanupGeneratedResourceCore(disposing, context);
                }
                catch (Exception ex)
                {
                    context.Capture(ex);
                }
            }
            finally
            {
                lock (_resourceLifecycleGate)
                {
                    _disposeOwnerThreadId = 0;
                    _disposeState = DisposedState;
                    Monitor.PulseAll(_resourceLifecycleGate);
                }

                if (disposing)
                    GC.SuppressFinalize(this);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Releases this resource and all resources it owns.
        /// </summary>
        /// <remarks>
        /// Calls made after cleanup has finished are no-ops. A concurrent cleanup, generated update, or node-port
        /// bind operation causes <see cref="InvalidOperationException"/> before cleanup starts; callers may retry
        /// after that operation ends. Generated owned resources are reserved as one graph before any cleanup hook
        /// runs, so a rejected cleanup leaves the complete graph live and unchanged.
        /// </remarks>
        public void Dispose()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            var context = new GeneratedResourceCleanupContext();
            lock (_resourceLifecycleGate)
            {
                if (_disposeState == DisposedState)
                    return;

                if (_disposeState == DisposingState)
                {
                    if (_disposeOwnerThreadId == currentThreadId)
                        return;

                    throw new InvalidOperationException("Resource cleanup is already in progress on another thread.");
                }

                if (_resourceOperationDepth != 0)
                {
                    throw new InvalidOperationException(
                        "A resource cannot be disposed while an update or node-port bind operation is in progress.");
                }

                _disposeState = DisposingState;
                _disposeOwnerThreadId = currentThreadId;
            }

            try
            {
                context.ReserveClaimedRoot(this, disposing: true);
            }
            catch
            {
                context.RollbackReservations();
                lock (_resourceLifecycleGate)
                {
                    if (_disposeState == DisposingState
                        && _disposeOwnerThreadId == currentThreadId)
                    {
                        _disposeOwnerThreadId = 0;
                        _disposeState = ActiveDisposeState;
                        Monitor.PulseAll(_resourceLifecycleGate);
                    }
                }
                throw;
            }

            context.DisposeAllReserved(this, disposing: true);
            context.FirstFailure?.Throw();
        }
    }

}
