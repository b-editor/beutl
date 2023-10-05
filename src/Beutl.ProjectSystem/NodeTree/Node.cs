using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Styling;
using Beutl.Utilities;

namespace Beutl.NodeTree;

public abstract class Node : Hierarchical
{
    public static readonly CoreProperty<bool> IsExpandedProperty;
    public static readonly CoreProperty<(double X, double Y)> PositionProperty;
    private readonly HierarchicalList<INodeItem> _items;
    private (double X, double Y) _position;
    private NodeTreeModel? _nodeTree;

    static Node()
    {
        IsExpandedProperty = ConfigureProperty<bool, Node>(nameof(Position))
            .DefaultValue(true)
            .Register();

        PositionProperty = ConfigureProperty<(double X, double Y), Node>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue((0, 0))
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

    [NotAutoSerialized]
    public (double X, double Y) Position
    {
        get => _position;
        set
        {
            double x = value.X;
            double y = value.Y;

            if (double.IsNaN(x))
                x = 0;
            if (double.IsNaN(y))
                y = 0;

            SetAndRaise(PositionProperty, ref _position, (x, y));
        }
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

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue(nameof(Position), out JsonNode? posNode)
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

        if (json.TryGetPropertyValue(nameof(Items), out var itemsNode)
            && itemsNode is JsonArray itemsArray)
        {
            int index = 0;
            foreach (JsonNode? item in itemsArray)
            {
                if (item is JsonObject itemObj)
                {
                    int localId;
                    if (itemObj.TryGetPropertyValue("LocalId", out var localIdNode)
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

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        json[nameof(Position)] = $"{Position.X},{Position.Y}";

        var array = new JsonArray();
        foreach (INodeItem item in Items)
        {
            var itemJson = new JsonObject();
            if (item is IJsonSerializable serializable)
            {
                serializable.WriteToJson(itemJson);
                itemJson.WriteDiscriminator(item.GetType());
            }
            array.Add(itemJson);
        }

        json[nameof(Items)] = array;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<string>(nameof(Position)) is { } posStr)
        {
            var tokenizer = new RefStringTokenizer(posStr);
            if (tokenizer.TryReadDouble(out double x)
                && tokenizer.TryReadDouble(out double y))
            {
                Position = (x, y);
            }
        }

        if (context.GetValue<JsonArray>(nameof(Items)) is { } itemsArray)
        {
            int index = 0;
            foreach (JsonNode? item in itemsArray)
            {
                if (item is JsonObject itemObj)
                {
                    int localId;
                    if (itemObj.TryGetPropertyValue("LocalId", out var localIdNode)
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

                    if (nodeItem is ICoreSerializable serializable)
                    {
                        if (LocalSerializationErrorNotifier.Current is not { } notifier)
                        {
                            notifier = NullSerializationErrorNotifier.Instance;
                        }
                        ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

                        var innerContext = new JsonSerializationContext(nodeItem.GetType(), notifier, parent, itemObj);
                        using (ThreadLocalSerializationContext.Enter(innerContext))
                        {
                            serializable.Deserialize(innerContext);
                        }
                    }
                }

                index++;
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Position), $"{Position.X},{Position.Y}");

        context.SetValue(nameof(Items), Items);
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (args.Parent is NodeTreeModel nodeTree)
        {
            _nodeTree = nodeTree;
            foreach (INodeItem item in _items.GetMarshal().Value)
            {
                item.NotifyAttachedToNodeTree(nodeTree);
            }
        }
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (args.Parent is NodeTreeModel nodeTree)
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
