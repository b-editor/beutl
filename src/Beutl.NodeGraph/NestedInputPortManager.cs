using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

// Owns the bindings independently of property-editor controls. Collapsing an editor must never
// remove a port or stop updating its value.
internal sealed class NestedInputPortManager(GraphNode node)
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly HashSet<PortKey> _overriddenTargets = [];
    private bool _synchronizing;
    private bool _pending;

    public bool IsSynchronizing => _synchronizing;

    public bool HasOverridingAncestor(INestedInputPort port)
        => _overriddenTargets.Contains(new PortKey(port.RootMember.Id, port.PropertyPath, port.AssociatedType));

    public void EnsureSynchronized(bool force = false)
    {
        if (_pending) Synchronize(force);
    }

    public void Synchronize(bool force = false)
    {
        if (_synchronizing) return;
        // A derived node restores its object after GraphNode.Deserialize returns. Keep the saved
        // ports intact until the entire object (including its list items) has been populated.
        if (!force && ThreadLocalSerializationContext.Current != null)
        {
            _pending = true;
            return;
        }
        _pending = false;
        _synchronizing = true;
        try
        {
            _subscriptions.Clear();
            _overriddenTargets.Clear();
            var targets = new List<Target>();
            var unresolved = new List<UnresolvedSubtree>();
            foreach (INodeMember member in node.Items)
            {
                if (member is not NodeMember root || member.Property is not { } adapter) continue;
                Watch(adapter);
                Visit(adapter.GetValue(), root, [], new HashSet<EngineObject>(ReferenceEqualityComparer.Instance),
                    targets, unresolved, IsValueOverridden(adapter));
            }

            var remaining = node.NestedInputPorts.ToHashSet();
            var existing = new Dictionary<PortKey, INestedInputPort>();
            foreach (INestedInputPort port in node.NestedInputPorts)
                existing.TryAdd(new PortKey(port.RootMember.Id, port.PropertyPath, port.AssociatedType), port);
            var added = new List<INestedInputPort>();
            foreach (Target target in targets)
            {
                if (target.HasOverridingAncestor)
                    _overriddenTargets.Add(new PortKey(target.Root.Id, target.Path, target.Property.ValueType));
                if (existing.Remove(new PortKey(target.Root.Id, target.Path, target.Property.ValueType), out var port))
                {
                    remaining.Remove(port);
                    port.Bind(target.Owner, target.Property);
                }
                else if (!RecordingSuppression.IsSuppressed)
                {
                    var portType = typeof(NestedInputPort<>).MakeGenericType(target.Property.ValueType);
                    port = (INestedInputPort)Activator.CreateInstance(portType,
                        target.Root, target.Path, target.Owner, target.Property)!;
                    added.Add(port);
                }
            }

            foreach (INestedInputPort port in remaining.ToArray())
            {
                if (unresolved.Any(subtree => subtree.Root.Id == port.RootMember.Id
                    && GraphNode.IsPathPrefix(subtree.Path, port.PropertyPath)))
                {
                    // A missing plugin cannot tell us which of its properties still exist. Keep
                    // the serialized endpoint and connection, but stop writing to the old target.
                    port.Unbind();
                    remaining.Remove(port);
                }
            }

            // During history replay the collection operations restore/remove the original port
            // instances themselves. Only rebind here; never race those operations with new IDs.
            if (!RecordingSuppression.IsSuppressed)
            {
                foreach (INestedInputPort port in remaining)
                {
                    if (port.Connection.Value is { } connection
                        && node.FindHierarchicalParent<GraphModel>() is { } graph)
                        graph.Disconnect(connection);
                }
                node.RemoveNestedInputPorts(remaining);
                node.NestedInputPorts.AddRange(added);
            }
        }
        finally
        {
            _synchronizing = false;
        }
        node.NotifyNestedInputPortsChanged();
    }

    public void Bind(INestedInputPort port)
    {
        INodeMember? root = node.Items.FirstOrDefault(m => m.Id == port.RootMember.Id);
        object? value = root?.Property?.GetValue();
        EngineObject? owner = null;
        IProperty? property = null;
        foreach (string segment in port.PropertyPath)
        {
            if (value is IFallback)
            {
                port.Unbind();
                return;
            }
            if (segment.StartsWith("p:", StringComparison.Ordinal) && value is EngineObject obj)
            {
                owner = obj;
                property = obj.Properties.FirstOrDefault(p => p.Name == segment[2..]);
                value = property?.CurrentValue;
            }
            else if (segment.StartsWith("i:", StringComparison.Ordinal) && value is IList list
                     && Guid.TryParse(segment.AsSpan(2), out Guid id))
            {
                value = list.OfType<EngineObject>().FirstOrDefault(item => GetItemId(item) == id);
                property = null;
            }
            else
            {
                port.Unbind();
                return;
            }
        }

        if (owner != null && property != null && property.ValueType == port.AssociatedType)
            port.Bind(owner, property);
        else
            port.Unbind();
    }

    private void Watch(IPropertyAdapter adapter)
    {
        object? value = adapter.GetValue();
        if (value is not EngineObject and not IList
            && !typeof(EngineObject).IsAssignableFrom(adapter.PropertyType)
            && !typeof(IEnumerable).IsAssignableFrom(adapter.PropertyType)) return;
        adapter.GetObservable().Skip(1).Subscribe(_ => Synchronize()).DisposeWith(_subscriptions);
        if (adapter.GetEngineProperty() is { } property)
        {
            IAnimation? previousAnimation = property.Animation;
            IExpression? previousExpression = property.Expression;
            EventHandler handler = (_, _) =>
            {
                if (ReferenceEquals(previousAnimation, property.Animation)
                    && ReferenceEquals(previousExpression, property.Expression)) return;
                previousAnimation = property.Animation;
                previousExpression = property.Expression;
                Synchronize();
            };
            property.Edited += handler;
            _subscriptions.Add(Disposable.Create(() => property.Edited -= handler));
        }
        else
        {
            if (adapter is IAnimatablePropertyAdapter animatable)
                animatable.ObserveAnimation.Skip(1).Subscribe(_ => Synchronize()).DisposeWith(_subscriptions);
            if (adapter is IExpressionPropertyAdapter expressible)
                expressible.ObserveExpression.Skip(1).Subscribe(_ => Synchronize()).DisposeWith(_subscriptions);
        }
    }

    private static bool IsValueOverridden(IPropertyAdapter adapter)
        => (adapter as IAnimatablePropertyAdapter)?.Animation != null
           || (adapter as IExpressionPropertyAdapter)?.Expression != null
           || adapter.GetEngineProperty() is { } property && (property.Animation != null || property.Expression != null);

    private void Visit(object? value, NodeMember root, string[] path,
        HashSet<EngineObject> ancestors, List<Target> targets, List<UnresolvedSubtree> unresolved, bool hasOverridingAncestor)
    {
        if (value is IFallback)
        {
            unresolved.Add(new UnresolvedSubtree(root, path));
        }
        else if (value is EngineObject obj)
        {
            if (!ancestors.Add(obj)) return;
            foreach (IProperty property in obj.GetDisplayProperties())
            {
                string[] childPath = [.. path, "p:" + property.Name];
                if (property.SupportsExpression)
                    targets.Add(new Target(root, childPath, obj, property, hasOverridingAncestor));
                var adapter = (IPropertyAdapter)Activator.CreateInstance(
                    typeof(EnginePropertyAdapter<>).MakeGenericType(property.ValueType), property, obj)!;
                Watch(adapter);
                Visit(property.CurrentValue, root, childPath, ancestors, targets, unresolved,
                    hasOverridingAncestor || property.Animation != null || property.Expression != null);
            }
            ancestors.Remove(obj);
        }
        else if (value is IList list)
        {
            if (list is INotifyCollectionChanged changed)
            {
                NotifyCollectionChangedEventHandler handler = (_, _) => Synchronize();
                changed.CollectionChanged += handler;
                _subscriptions.Add(Disposable.Create(() => changed.CollectionChanged -= handler));
            }
            // Duplicate occurrences of an object still refer to the same property, not two inputs.
            foreach (EngineObject item in list.OfType<EngineObject>().DistinctBy(GetItemId))
                Visit(item, root, [.. path, "i:" + GetItemId(item)], ancestors, targets, unresolved, hasOverridingAncestor);
        }
    }

    private static Guid GetItemId(EngineObject item)
        => item is IFallback { Json: { } json } && json[nameof(CoreObject.Id)] is JsonValue value
            && value.TryGetValue<Guid>(out Guid id) ? id : item.Id;

    private sealed record Target(NodeMember Root, string[] Path, EngineObject Owner, IProperty Property, bool HasOverridingAncestor);
    private sealed record UnresolvedSubtree(NodeMember Root, string[] Path);

    private readonly record struct PortKey(Guid RootId, IReadOnlyList<string> Path, Type? Type)
    {
        public bool Equals(PortKey other)
            => RootId == other.RootId && Type == other.Type && Path.SequenceEqual(other.Path);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(RootId);
            hash.Add(Type);
            foreach (string segment in Path) hash.Add(segment, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
