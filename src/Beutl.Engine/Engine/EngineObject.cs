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
    // CoreProperty rather than IProperty: these change only through edits and are not evaluated per frame.
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
    // The entries of Animations and Expressions that Deserialize does not read, usually for properties this type
    // does not have, kept as unknown members are (see CoreObject.UnknownMembers), so the animation or expression a
    // newer version gave a property survives an older version too.
    private JsonObject? _unknownAnimations;
    private JsonObject? _unknownExpressions;

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

    // ZIndex and TimeRange follow the element or parent object, so replaying its edits restores them.
    [NotAutoSerialized]
    [NotTracked]
    public int ZIndex
    {
        get => _zIndex;
        set => SetAndRaise(ZIndexProperty, ref _zIndex, value);
    }

    [NotAutoSerialized]
    [NotTracked]
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

        Dictionary<string, IKeyFrameAnimation>? animations
            = context.GetValue<Dictionary<string, IKeyFrameAnimation>>("Animations");

        Dictionary<string, JsonNode>? expressions
            = context.GetValue<Dictionary<string, JsonNode>>("Expressions");

        foreach (IProperty property in _properties)
        {
            if (!IsAutoSerialized(property))
                continue;

            // A property renamed with a FormerPersistedNameAttribute reads its value, animation and expression
            // under the former name when the current one is not there.
            property.DeserializeValue(context);
            Attribute[]? attributes = property.GetAttributes();
            if (property.IsAnimatable
                && FormerPersistedNames.TryGetMember(animations, property.Name, attributes, out IKeyFrameAnimation? animation))
            {
                property.SetKeyFrameAnimation(animation);
            }

            if (FormerPersistedNames.TryGetMember(expressions, property.Name, attributes, out JsonNode? expressionNode))
            {
                property.DeserializeExpression(expressionNode);
            }
        }

        // A fallback keeps them whatever the options say, as it keeps its unknown members.
        if (context is JsonSerializationContext jsonContext && (this is IFallback || jsonContext.KeepsUnknownMembers))
        {
            KeepUnknownEntries(
                jsonContext, "Animations", static property => property.IsAnimatable, ref _unknownAnimations);
            KeepUnknownEntries(jsonContext, "Expressions", static _ => true, ref _unknownExpressions);
        }
    }

    // Keeps the entries of the name member that Deserialize does not read: it reads an entry for an auto-serialized
    // property that takes one (takes), under the property's name or a former one. Any other entry is kept and
    // written back, such as the animation of a property that a newer version made animatable, but not one read under
    // a former name, which would be written back next to the current one. Like unknown members, entries read again
    // replace the ones kept before, and a populate without the member keeps them.
    private void KeepUnknownEntries(
        JsonSerializationContext context, string name, Func<IProperty, bool> takes, ref JsonObject? kept)
    {
        if (context.GetNode(name) is not JsonObject entries) return;

        foreach ((string key, JsonNode? node) in entries)
        {
            if (_properties.Exists(property => IsAutoSerialized(property)
                                               && takes(property)
                                               && FormerPersistedNames.IsMemberKey(
                                                   key, property.Name, property.GetAttributes())))
            {
                continue;
            }

            (kept ??= [])[key] = node?.DeepClone();
            RootFileVersions.RecordKeptUnknownData();
        }
    }

    // Writes the entries KeepUnknownEntries kept back into the name member, after this object's own entries.
    private static void WriteUnknownEntries(ICoreSerializationContext context, string name, JsonObject? kept)
    {
        if (kept is null
            || context is not JsonSerializationContext { KeepsUnknownMembers: true } jsonContext
            || jsonContext.GetNode(name) is not JsonObject entries)
        {
            return;
        }

        foreach ((string key, JsonNode? node) in kept)
        {
            if (!entries.ContainsKey(key))
            {
                entries[key] = node?.DeepClone();
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

        Dictionary<string, IKeyFrameAnimation> animations = serialized
            .Where(p => p.IsAnimatable)
            .Select(p => (p.Name, Animation: p.GetKeyFrameAnimation()))
            .Where(p => p.Animation is not null)
            .ToDictionary(p => p.Name, p => p.Animation!);

        context.SetValue("Animations", animations);
        WriteUnknownEntries(context, "Animations", _unknownAnimations);

        Dictionary<string, JsonNode> expressions = serialized
            .Select(p => (Name: p.Name, Node: p.SerializeExpression()))
            .Where(p => p.Node is not null)
            .ToDictionary(p => p.Name, p => p.Node!);

        context.SetValue("Expressions", expressions);
        WriteUnknownEntries(context, "Expressions", _unknownExpressions);

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
        /// Only <see cref="Reconcile"/> moves it. A caller that edits a hand-built resource bumps it themselves.
        /// </remarks>
        public int Version { get; set; }

        /// <summary>
        /// Whether the engine acts on this resource at all.
        /// </summary>
        /// <remarks>Setting it does not move <see cref="Version"/>.</remarks>
        public bool IsEnabled { get; set; }

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets whether this resource has a backing engine object.
        /// </summary>
        /// <remarks>Only <see cref="Reconcile"/> attaches one; a resource built through its constructor is detached.</remarks>
        public bool IsAttached => _original is not null;

        /// <summary>
        /// Gets the object this resource was built from, or <see langword="null"/> when
        /// <see cref="IsAttached"/> is <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// Code that only needs a stable key uses <c>EngineResourceIdentity</c>; use <see cref="RequireOriginal"/>
        /// when a detached resource cannot be handled.
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
