using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reflection;
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
            if (!IsAutoSerialized(property))
                continue;

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

        IProperty[] serialized = [.. _properties.Where(IsAutoSerialized)];

        Dictionary<string, IAnimation> animations = serialized
            .Where(p => p is { IsAnimatable: true, Animation: not null })
            .ToDictionary(p => p.Name, p => p.Animation!);

        context.SetValue("Animations", animations);

        Dictionary<string, JsonNode> expressions = serialized
            .Select(p => (Name: p.Name, Node: p.SerializeExpression()))
            .Where(p => p.Node is not null)
            .ToDictionary(p => p.Name, p => p.Node!);

        context.SetValue("Expressions", expressions);

        foreach (IProperty property in serialized)
        {
            property.SerializeValue(context);
        }
    }

    /// <summary>Whether <paramref name="property"/> is saved with this object.</summary>
    /// <remarks>
    /// <see cref="NotAutoSerializedAttribute"/> means here what it means on a <see cref="CoreProperty"/>: the value
    /// is state outside the document, such as an editor's, so it is neither written nor read back.
    /// </remarks>
    private static bool IsAutoSerialized(IProperty property)
        => property.GetAttributes()?.Any(static attribute => attribute is NotAutoSerializedAttribute) != true;

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
        => MovePropertyNextTo(property, target, offset: 0);

    protected void MovePropertyAfter(IProperty property, IProperty target)
        => MovePropertyNextTo(property, target, offset: 1);

    // A target that is not displayed sends the property to the end.
    private void MovePropertyNextTo(IProperty property, IProperty target, int offset)
    {
        EnsureDisplayProperties();
        _displayProperties.Remove(property);
        int targetIndex = _displayProperties.IndexOf(target);
        if (targetIndex >= 0)
            _displayProperties.Insert(targetIndex + offset, property);
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
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public class Resource : IDisposable
    {
        ~Resource()
        {
            Dispose(false);
        }

        private EngineObject? _original;

        internal virtual IReadOnlyList<FlowNode> FlowInputs => [];

        internal FlowNode? CapturedFlow { get; set; }

        /// <summary>
        /// The number every cache over this resource keys on.
        /// </summary>
        /// <remarks>
        /// A change to this number invalidates such a cache, and nothing else does. Reconciling against an
        /// engine object is the one thing that moves it on its own: <see cref="Reconcile"/> and the
        /// <see cref="ResourceReconciler"/> methods it calls step it whenever a parameter of this resource or of
        /// one it owns changed, so an attached resource asks nothing of its caller. No setter moves it. A resource built
        /// by hand never reconciles, so nothing moves it there at all - assigning a property stores the
        /// value and stops, and resource lists are handed out as plain <see cref="List{T}"/>, so a caller
        /// reaches the children the same way. Moving it is then the caller's job: whoever edits a hand-built
        /// resource - setting one of its properties, or adding to, removing from, reordering, or mutating a
        /// child of it - bumps its version themselves, or every cache keyed on it goes on serving what it
        /// built before the edit.
        /// </remarks>
        public int Version { get; set; }

        /// <summary>
        /// Whether the engine acts on this resource at all.
        /// </summary>
        /// <remarks>
        /// Assigning this stores the value and does nothing else, as every other setter here does.
        /// Reconciling moves <see cref="Version"/> when it copies a changed value across; a caller that sets
        /// this on a hand-built resource moves the version themselves.
        /// </remarks>
        public bool IsEnabled { get; set; }

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets whether this resource has a backing engine object.
        /// </summary>
        /// <remarks>
        /// Only <see cref="Reconcile"/> attaches one, so a resource built through its public constructor rather
        /// than through <see cref="ToResource"/> is detached.
        /// </remarks>
        public bool IsAttached => _original is not null;

        /// <summary>
        /// Gets the object this resource was built from, or <see langword="null"/> when
        /// <see cref="IsAttached"/> is <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// A resource constructed directly instead of through <see cref="EngineObject.ToResource"/> - a
        /// detached resource - never receives a backing object. Engine code that only needs an
        /// equality-stable key uses <c>EngineResourceIdentity</c> instead, which handles that case. Use
        /// <see cref="RequireOriginal"/> when a missing backing object cannot be handled.
        /// </remarks>
        public EngineObject? GetOriginal() => _original;

        /// <summary>
        /// Gets the backing engine object, throwing when this resource is detached.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// This resource has no backing engine object.
        /// </exception>
        public EngineObject RequireOriginal()
        {
            return _original ?? throw new InvalidOperationException(
                $"{GetType()} was constructed directly rather than through {nameof(EngineObject)}.{nameof(ToResource)}, "
                + "so it has no backing engine object to dispatch to.");
        }

        /// <summary>
        /// Brings this resource in line with <paramref name="obj"/> at <paramref name="context"/>'s time and
        /// attaches <paramref name="obj"/> as its backing object.
        /// </summary>
        /// <param name="versionBumped">
        /// Whether <see cref="Version"/> has already moved during this pass; the first change sets it, so later
        /// changes need not move the version again. <see cref="EngineObject.ToResource"/> passes
        /// <see langword="true"/>, since nothing is keyed on a resource that was just built.
        /// </param>
        public virtual void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _original = obj;
            if (IsEnabled != obj.IsEnabled)
            {
                IsEnabled = obj.IsEnabled;
                BumpVersion(ref versionBumped);
            }
        }

        // Moves Version for the first change a reconcile pass finds; versionBumped then suppresses further bumps.
        internal void BumpVersion(ref bool versionBumped)
        {
            if (!versionBumped)
            {
                Version++;
                versionBumped = true;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            Dispose(true);
            CapturedFlow = null;
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

}
