using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Framework;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Styling;
using Beutl.Utilities;

using Reactive.Bindings.Extensions;

namespace Beutl.NodeTree;

public sealed class SetterPropertyImpl<T> : IAbstractAnimatableProperty<T>
{
    private sealed class HasAnimationObservable : LightweightObservableBase<bool>
    {
        private IDisposable? _disposable;
        private readonly Setter<T> _setter;

        public HasAnimationObservable(Setter<T> setter)
        {
            _setter = setter;
        }

        protected override void Subscribed(IObserver<bool> observer, bool first)
        {
            base.Subscribed(observer, first);
            observer.OnNext(_setter.Animation is { Children.Count: > 0 });
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;

            _setter.Invalidated -= Setter_Invalidated;
        }

        protected override void Initialize()
        {
            _disposable?.Dispose();

            _setter.Invalidated += Setter_Invalidated;
        }

        private void Setter_Invalidated(object? sender, EventArgs e)
        {
            _disposable?.Dispose();
            if (_setter.Animation is { } animation)
            {
                _disposable = _setter.Animation.Children
                    .ObserveProperty(x => x.Count)
                    .Select(x => x > 0)
                    .Subscribe(PublishNext);
            }
        }
    }

    public SetterPropertyImpl(Setter<T> setter, Type implementedType)
    {
        Property = setter.Property;
        Setter = setter;
        ImplementedType = implementedType;
        HasAnimation = new HasAnimationObservable(setter);
    }

    public CoreProperty<T> Property { get; }

    public Setter<T> Setter { get; }

    public Animation<T> Animation
    {
        get
        {
            Setter.Animation ??= new Animation<T>(Property);
            return Setter.Animation;
        }
    }

    public IObservable<bool> HasAnimation { get; }

    public Type ImplementedType { get; }

    public IObservable<T?> GetObservable()
    {
        return Setter;
    }

    public T? GetValue()
    {
        return Setter.Value;
    }

    public void SetValue(T? value)
    {
        Setter.Value = value;
    }

    public void WriteToJson(ref JsonNode json)
    {
        json["property"] = Property.Name;
        json["target"] = TypeFormat.ToString(ImplementedType);

        json["setter"] = StyleSerializer.ToJson(Setter, ImplementedType).Item2;
    }

    public void ReadFromJson(JsonNode json)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("setter", out var setterNode)
                && setterNode != null)
            {
                if (StyleSerializer.ToSetter(setterNode, Property.Name, ImplementedType) is Setter<T> setter)
                {
                    if (setter.Animation != null)
                    {
                        if (Setter.Animation == null)
                        {
                            Setter.Animation = setter.Animation;
                        }
                        else
                        {
                            Setter.Animation.Children.Clear();
                            Setter.Animation.Children.Replace(setter.Animation.Children);
                        }
                    }

                    Setter.Value = setter.Value;
                }
            }
        }
    }
}


internal interface IInputSocketForSetter : ICoreObject, IInputSocket
{
    new int LocalId { get; set; }

    void SetProperty(object property);
}

public sealed class NodeItemForSetter<T> : NodeItem<T>
{
    public void SetProperty(SetterPropertyImpl<T> property)
    {
        Property = property;
        property.Setter.Invalidated += OnSetterInvalidated;
    }

    private void OnSetterInvalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    public SetterPropertyImpl<T>? GetProperty()
    {
        return Property as SetterPropertyImpl<T>;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        GetProperty()?.ReadFromJson(json);
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        GetProperty()?.WriteToJson(ref json);
    }
}

public sealed class InputSocketForSetter<T> : InputSocket<T>, IInputSocketForSetter
{
    public void SetProperty(SetterPropertyImpl<T> property)
    {
        Property = property;
        property.Setter.Invalidated += OnSetterInvalidated;
    }

    void IInputSocketForSetter.SetProperty(object property)
    {
        var obj = (SetterPropertyImpl<T>)property;
        Property = obj;
        obj.Setter.Invalidated += OnSetterInvalidated;
    }

    private void OnSetterInvalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    public SetterPropertyImpl<T>? GetProperty()
    {
        return Property as SetterPropertyImpl<T>;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        GetProperty()?.ReadFromJson(json);
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        GetProperty()?.WriteToJson(ref json);
    }
}

public abstract class Node : Element, INode
{
    public static readonly CoreProperty<bool> IsExpandedProperty;
    public static readonly CoreProperty<(double X, double Y)> PositionProperty;
    private readonly LogicalList<INodeItem> _items;
    private (double X, double Y) _position;
    private NodeTreeSpace? _nodeTree;

    static Node()
    {
        IsExpandedProperty = ConfigureProperty<bool, Node>(nameof(Position))
            .DefaultValue(true)
            .SerializeName("is-expanded")
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        PositionProperty = ConfigureProperty<(double X, double Y), Node>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue((0, 0))
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();
    }

    public Node()
    {
        _items = new(this);

        _items.Attached += OnItemAttached;
        _items.Detached += OnItemDetached;
    }

    private void OnItemDetached(INodeItem obj)
    {
        obj.NodeTreeInvalidated -= OnItemNodeTreeInvalidated;
        obj.Invalidated -= OnItemInvalidated;
        if (_nodeTree != null)
        {
            obj.NotifyDetachedFromNodeTree(_nodeTree);
        }
    }

    private void OnItemAttached(INodeItem obj)
    {
        obj.NodeTreeInvalidated += OnItemNodeTreeInvalidated;
        obj.Invalidated += OnItemInvalidated;
        if (_nodeTree != null)
        {
            obj.NotifyAttachedToNodeTree(_nodeTree);
        }
    }

    private void OnItemInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }

    public ICoreList<INodeItem> Items => _items;

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public (double X, double Y) Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    protected int NextLocalId { get; set; } = 0;

    public event EventHandler? NodeTreeInvalidated;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    protected void InvalidateNodeTree()
    {
        NodeTreeInvalidated?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

    public virtual void InitializeForContext(NodeEvaluationContext context)
    {
    }

    public virtual void UninitializeForContext(NodeEvaluationContext context)
    {
    }

    // 1. ItemsのIInputSocket.Connection.Nodeを評価する。
    // 2. IOutputSocket.ConnectionsからIInputSocketにデータを送る (Receive)
    public virtual void Evaluate(NodeEvaluationContext context)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            INodeItem item = Items[i];
            item.Evaluate(context);
        }
    }

    public virtual void PreEvaluate(NodeEvaluationContext context)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            INodeItem item = Items[i];
            item.PreEvaluate(context);
        }
    }

    public virtual void PostEvaluate(NodeEvaluationContext context)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            INodeItem item = Items[i];
            item.PostEvaluate(context);
        }
    }

    protected InputSocket<T> AsInput<T>(CoreProperty<T> property, int localId = -1)
    {
        InputSocket<T> socket = CreateInput<T>(property, localId);
        Items.Add(socket);
        return socket;
    }

    protected IInputSocket AsInput(CoreProperty property, int localId = -1)
    {
        IInputSocket socket = CreateInput(property, localId);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T, TOwner>(CoreProperty<T> property, int localId = -1)
    {
        InputSocket<T> socket = CreateInput<T, TOwner>(property, localId);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T>(CoreProperty<T> property, T value, int localId = -1)
    {
        InputSocket<T> socket = CreateInput(property, value, localId);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T, TOwner>(CoreProperty<T> property, T value, int localId = -1)
    {
        InputSocket<T> socket = CreateInput<T, TOwner>(property, value, localId);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T>(string name, int localId = -1)
    {
        InputSocket<T> socket = CreateInput<T>(name, localId);
        Items.Add(socket);
        return socket;
    }

    protected IInputSocket AsInput(string name, Type type, int localId = -1)
    {
        IInputSocket socket = CreateInput(name, type, localId);
        Items.Add(socket);
        return socket;
    }

    protected OutputSocket<T> AsOutput<T>(string name, T value, int localId = -1)
    {
        OutputSocket<T> socket = CreateOutput<T>(name, value, localId);
        Items.Add(socket);
        return socket;
    }

    protected OutputSocket<T> AsOutput<T>(string name, int localId = -1)
    {
        OutputSocket<T> socket = CreateOutput<T>(name, localId);
        Items.Add(socket);
        return socket;
    }

    protected NodeItem<T> AsProperty<T>(CoreProperty<T> property, int localId = -1)
    {
        NodeItem<T> socket = CreateProperty(property, localId);
        Items.Add(socket);
        return socket;
    }

    protected NodeItem<T> AsProperty<T>(CoreProperty<T> property, T value, int localId = -1)
    {
        NodeItem<T> socket = CreateProperty(property, value, localId);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> CreateInput<T>(CoreProperty<T> property, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new InputSocketForSetter<T>() { LocalId = localId };
        socket.SetProperty(propImpl);
        return socket;
    }

    protected IInputSocket CreateInput(CoreProperty property, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        Type propertyType = property.PropertyType;
        object setter = Activator.CreateInstance(typeof(Setter<>).MakeGenericType(propertyType), property)!;
        object propImpl = Activator.CreateInstance(typeof(SetterPropertyImpl<>).MakeGenericType(propertyType), setter, property.OwnerType)!;
        var socket = (IInputSocketForSetter)Activator.CreateInstance(typeof(InputSocketForSetter<>).MakeGenericType(propertyType))!;
        socket.LocalId = localId;
        socket.SetProperty(propImpl);
        return socket;
    }

    protected InputSocket<T> CreateInput<T, TOwner>(CoreProperty<T> property, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, typeof(TOwner));
        var socket = new InputSocketForSetter<T>() { LocalId = localId };
        socket.SetProperty(propImpl);
        return socket;
    }

    protected InputSocket<T> CreateInput<T>(CoreProperty<T> property, T value, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new InputSocketForSetter<T>() { LocalId = localId };
        socket.SetProperty(propImpl);
        return socket;
    }

    protected InputSocket<T> CreateInput<T, TOwner>(CoreProperty<T> property, T value, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, typeof(TOwner));
        var socket = new InputSocketForSetter<T>() { LocalId = localId };
        socket.SetProperty(propImpl);
        return socket;
    }

    protected InputSocket<T> CreateInput<T>(string name, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        return new InputSocketForSetter<T>()
        {
            Name = name,
            LocalId = localId
        };
    }

    protected IInputSocket CreateInput(string name, Type type, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var socket = (IInputSocketForSetter)Activator.CreateInstance(typeof(InputSocketForSetter<>).MakeGenericType(type))!;
        socket.Name = name;
        socket.LocalId = localId;
        return socket;
    }

    protected OutputSocket<T> CreateOutput<T>(string name, T value, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        return new OutputSocket<T>()
        {
            Name = name,
            LocalId = localId,
            Value = value
        };
    }

    protected OutputSocket<T> CreateOutput<T>(string name, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        return new OutputSocket<T>()
        {
            Name = name,
            LocalId = localId
        };
    }

    protected IOutputSocket CreateOutput(string name, Type type, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var socket = (IOutputSocket)Activator.CreateInstance(typeof(OutputSocket<>).MakeGenericType(type))!;
        if (socket is NodeItem nodeItem)
        {
            nodeItem.Name = name;
            nodeItem.LocalId = localId;
        }
        return socket;
    }

    protected NodeItem<T> CreateProperty<T>(CoreProperty<T> property, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new NodeItemForSetter<T>();
        socket.SetProperty(propImpl);
        socket.LocalId = localId;
        return socket;
    }

    protected NodeItem<T> CreateProperty<T>(CoreProperty<T> property, T value, int localId = -1)
    {
        localId = GetLocalId(localId);

        if (ValidateLocalId(localId))
            throw new InvalidOperationException("An item with the same local-id already exists.");

        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new NodeItemForSetter<T>();
        socket.SetProperty(propImpl);
        socket.LocalId = localId;
        return socket;
    }

    private bool ValidateLocalId(int localId)
    {
        return Items.Any(x => x.LocalId == localId);
    }

    private int GetLocalId(int requestedLocalId)
    {
        requestedLocalId = Math.Max(requestedLocalId, NextLocalId);
        NextLocalId++;
        return requestedLocalId;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("position", out JsonNode? posNode)
                && posNode is JsonValue posVal
                && posVal.TryGetValue(out string? posStr))
            {
                var tokenizer = new RefStringTokenizer(posStr);
                if (tokenizer.TryReadDouble(out double x)
                    && tokenizer.TryReadDouble(out double y))
                {
                    Position = (x, y);
                }
            }

            if (obj.TryGetPropertyValue("items", out var itemsNode)
                && itemsNode is JsonArray itemsArray)
            {
                int index = 0;
                foreach (JsonNode? item in itemsArray)
                {
                    if (item is JsonObject itemObj)
                    {
                        int localId;
                        if (itemObj.TryGetPropertyValue("local-id", out var localIdNode)
                            && localIdNode is JsonValue localIdValue
                            && localIdValue.TryGetValue(out int actualLId))
                        {
                            localId = actualLId;
                        }
                        else
                        {
                            localId = index;
                        }

                        INodeItem? nodeItem = Items.FirstOrDefault(x => x.LocalId == localId);

                        if (nodeItem is IJsonSerializable serializable)
                        {
                            serializable.ReadFromJson(itemObj);
                        }
                    }

                    index++;
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        json["position"] = $"{Position.X},{Position.Y}";

        var array = new JsonArray();
        foreach (INodeItem item in Items)
        {
            JsonNode itemJson = new JsonObject();
            if (item is IJsonSerializable serializable)
            {
                serializable.WriteToJson(ref itemJson);
                itemJson["@type"] = TypeFormat.ToString(item.GetType());
            }
            array.Add(itemJson);
        }

        json["items"] = array;
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        if (args.Parent is NodeTreeSpace nodeTree)
        {
            _nodeTree = nodeTree;
            foreach (INodeItem item in _items.GetMarshal().Value)
            {
                item.NotifyAttachedToNodeTree(nodeTree);
            }
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (args.Parent is NodeTreeSpace nodeTree)
        {
            _nodeTree = null;
            foreach (INodeItem item in _items.GetMarshal().Value)
            {
                item.NotifyDetachedFromNodeTree(nodeTree);
            }
        }
    }

    private void OnItemNodeTreeInvalidated(object? sender, EventArgs e)
    {
        InvalidateNodeTree();
    }
}
