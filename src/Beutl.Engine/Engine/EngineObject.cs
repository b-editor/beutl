using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Engine;

public sealed partial class FallbackEngineObject : EngineObject, IFallback;

[FallbackType(typeof(FallbackEngineObject))]
public partial class EngineObject : Hierarchical, INotifyEdited, IExpressionJsonCodec
{
    private IDisposable? _timeAnchorSubscription;
    private List<IProperty>? _displayProperties;
    // The entries of Animations and Expressions that Deserialize does not read, usually for properties this type
    // does not have, kept as unknown members are (see CoreObject.UnknownMembers), so the animation or expression a
    // newer version gave a property survives an older version too.
    private JsonObject? _unknownAnimations;
    private JsonObject? _unknownExpressions;

    public event EventHandler? Edited;

    public EngineObject()
    {
        ScanProperties<EngineObject>();
        HideProperties(IsTimeAnchor, IsEnabled, ZIndex, TimeRange, Id, Name);
        IsTimeAnchor.ValueChanged += (_, e) =>
        {
            if (e.NewValue)
            {
                RevokeTimeAnchorSubscription();
            }
            else
            {
                SubscribeTimeAnchor();
            }
        };
    }

    IExpression<T>? IExpressionJsonCodec.Read<T>(JsonNode node) => Expressions.Expression.CreateFromNode<T>(node);

    JsonNode IExpressionJsonCodec.Write<T>(IExpression<T> expression) => Expressions.Expression.ToNode(expression);

    [NotAutoSerialized]
    public IProperty<bool> IsTimeAnchor { get; } = Property.CreateValue(false);

    public IProperty<bool> IsEnabled { get; } = Property.CreateValue(true);

    // ZIndex and TimeRange follow the element or parent object, so replaying its edits restores them.
    [NotAutoSerialized]
    [NotTracked]
    public IProperty<int> ZIndex { get; } = Property.CreateValue(0);

    [NotAutoSerialized]
    [NotTracked]
    public IProperty<TimeRange> TimeRange { get; } = Property.CreateValue(default(TimeRange));

    public MediaTime Start => TimeRange.Value.Start;

    public MediaTime Duration => TimeRange.Value.Duration;

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        if (IsTimeAnchor.Value) return;

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

        var d1 = parent.TimeRange.GetObservable()
            .Subscribe(t => TimeRange.Value = t);

        var d2 = parent.ZIndex.GetObservable()
            .Subscribe(z => ZIndex.Value = z);

        _timeAnchorSubscription = Disposable.Create((d1, d2), t => t.DisposeAll());
    }

    internal override void DeserializeMembers(ICoreSerializationContext context)
    {
        var start = context.GetValue<Optional<MediaTime>>(nameof(Media.TimeRange.Start));
        var duration = context.GetValue<Optional<MediaTime>>(nameof(Media.TimeRange.Duration));
        var zIndex = context.GetValue<Optional<int>>(nameof(ZIndex));
        if (start.HasValue && duration.HasValue)
        {
            TimeRange.Value = new TimeRange(start.Value, MediaTimeJsonConverter.LengthRead(
                context, nameof(Media.TimeRange.Start), nameof(Media.TimeRange.Duration), start.Value, duration.Value));
        }

        if (zIndex.HasValue)
            ZIndex.Value = zIndex.Value;
        IsTimeAnchor.Value = start.HasValue && duration.HasValue && zIndex.HasValue;

        Dictionary<string, IPropertyAnimation>? animations
            = context.GetValue<Dictionary<string, IPropertyAnimation>>("Animations");

        Dictionary<string, JsonNode>? expressions
            = context.GetValue<Dictionary<string, JsonNode>>("Expressions");

        foreach (IProperty property in RegisteredProperties)
        {
            // A property renamed with a FormerPersistedNameAttribute reads its animation and expression under the
            // former name when the current one is not there.
            if (!IsAutoSerialized(property) || property is not IExpressionProperty expressionProperty)
                continue;

            Attribute[]? attributes = property.GetAttributes();
            if (expressionProperty.IsAnimatable
                && FormerPersistedNames.TryGetMember(animations, property.Name, attributes, out IPropertyAnimation? animation))
            {
                property.SetPropertyAnimation(animation);
            }

            if (FormerPersistedNames.TryGetMember(expressions, property.Name, attributes, out JsonNode? expressionNode))
            {
                expressionProperty.DeserializeExpression(expressionNode);
            }
        }

        // A fallback keeps them whatever the options say, as it keeps its unknown members.
        if (context is JsonSerializationContext jsonContext && (this is IFallback || jsonContext.KeepsUnknownMembers))
        {
            KeepUnknownEntries(
                jsonContext, "Animations", static property => property is IExpressionProperty { IsAnimatable: true }, ref _unknownAnimations);
            KeepUnknownEntries(jsonContext, "Expressions", static property => property is IExpressionProperty, ref _unknownExpressions);
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
            if (RegisteredProperties.Any(property => IsAutoSerialized(property)
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

    internal override void SerializeMembers(ICoreSerializationContext context, IProperty[] saved)
    {
        if (IsTimeAnchor.Value)
        {
            context.SetValue(nameof(Media.TimeRange.Start), TimeRange.Value.Start);
            context.SetValue(nameof(Media.TimeRange.Duration), MediaTimeJsonConverter.LengthToWrite(TimeRange.Value.Start, TimeRange.Value.Duration));
            context.SetValue(nameof(ZIndex), ZIndex.Value);
        }

        Dictionary<string, IPropertyAnimation> animations = saved
            .Where(p => p is IExpressionProperty { IsAnimatable: true })
            .Select(p => (p.Name, Animation: p.GetPropertyAnimation()))
            .Where(p => p.Animation is not null)
            .ToDictionary(p => p.Name, p => p.Animation!);

        context.SetValue("Animations", animations);
        WriteUnknownEntries(context, "Animations", _unknownAnimations);

        Dictionary<string, JsonNode> expressions = saved
            .Select(p => (Name: p.Name, Node: (p as IExpressionProperty)?.SerializeExpression()))
            .Where(p => p.Node is not null)
            .ToDictionary(p => p.Name, p => p.Node!);

        context.SetValue("Expressions", expressions);
        WriteUnknownEntries(context, "Expressions", _unknownExpressions);
    }

    // An engine object's base fields are the value-only properties and lists it hides from its panels.
    internal override bool IsBaseField(IProperty property)
        => property is not IExpressionProperty && _displayProperties?.Contains(property) == false;

    protected override void RegisterProperty(IProperty property, int index)
    {
        bool added = !RegisteredProperties.Contains(property);
        base.RegisterProperty(property, index);
        if (added)
        {
            property.Edited += OnPropertyEdited;
        }

        if (_displayProperties?.Contains(property) == false)
        {
            _displayProperties.Insert(Math.Min(index, _displayProperties.Count), property);
        }
    }

    public IReadOnlyList<IProperty> GetDisplayProperties() => _displayProperties ?? RegisteredProperties;

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
        _displayProperties ??= [.. RegisteredProperties];
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

        internal virtual EngineObject? FlowOwner => null;

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
            if (IsEnabled != obj.IsEnabled.Value)
            {
                IsEnabled = obj.IsEnabled.Value;
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

        // Lets a resource evaluate a child property its own way while an input sample evaluates a drawable.
        // Returning false reconciles the child as usual.
        internal virtual bool TryReconcileSampledChild(IProperty property, CompositionContext context, ref Resource? field)
            => false;

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
