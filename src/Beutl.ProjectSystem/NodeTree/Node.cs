using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.NodeTree;

public abstract class Node : Hierarchical
{
    public static readonly CoreProperty<bool> IsExpandedProperty;
    public static readonly CoreProperty<(double X, double Y)> PositionProperty;
    public static readonly CoreProperty<ICoreList<INodeItem>> ItemsProperty;
    private readonly HierarchicalList<INodeItem> _items;
    private (double X, double Y) _position;

    static Node()
    {
        IsExpandedProperty = ConfigureProperty<bool, Node>(nameof(IsExpanded))
            .DefaultValue(true)
            .Register();

        PositionProperty = ConfigureProperty<(double X, double Y), Node>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue((0, 0))
            .Register();

        ItemsProperty = ConfigureProperty<ICoreList<INodeItem>, Node>(nameof(Items))
            .Accessor(o => o.Items, (o, v) => o._items.Replace(v))
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
        obj.TopologyChanged -= OnItemTopologyChanged;
        obj.Edited -= OnItemEdited;
    }

    private void OnItemAttached(INodeItem obj)
    {
        obj.TopologyChanged += OnItemTopologyChanged;
        obj.Edited += OnItemEdited;
    }

    private void OnItemEdited(object? sender, EventArgs e)
    {
        RaiseEdited(e);
    }

    private void OnItemTopologyChanged(object? sender, EventArgs e)
    {
        RaiseTopologyChanged();
    }

    [NotAutoSerialized]
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

    public event EventHandler? TopologyChanged;

    public event EventHandler? Edited;

    protected void RaiseTopologyChanged()
    {
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseEdited(EventArgs args)
    {
        Edited?.Invoke(this, args);
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

    // TODO: AddInput, AddOutput, AddPropertyに変更する
    protected InputSocket<T> AddInput<T>(EngineObject obj, IProperty<T> property)
    {
        var socket = new EnginePropertyBackedInputSocket<T>(obj, property);
        Items.Add(socket);
        return socket;
    }

    protected IInputSocket AddInput(EngineObject obj, IProperty property)
    {
        var type = typeof(EnginePropertyBackedInputSocket<>).MakeGenericType(property.ValueType);
        var socket = (IInputSocket)Activator.CreateInstance(type, obj, property)!;
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AddInput<T>(string name, DisplayAttribute? display = null)
    {
        InputSocket<T> socket = CreateInput<T>(name, display);
        Items.Add(socket);
        return socket;
    }

    protected IInputSocket AddInput(string name, Type type, DisplayAttribute? display = null)
    {
        IInputSocket socket = CreateInput(name, type, display);
        Items.Add(socket);
        return socket;
    }

    protected ListInputSocket<T> AddListInput<T>(string name, DisplayAttribute? display = null)
    {
        var socket = new ListInputSocket<T>() { Name = name, Display = display };
        Items.Add(socket);
        return socket;
    }

    protected ListOutputSocket<T> AddListOutput<T>(string name, DisplayAttribute? display = null)
    {
        var socket = new ListOutputSocket<T>() { Name = name, Display = display };
        Items.Add(socket);
        return socket;
    }

    protected IListInputSocket AddListInput(string name, Type type, DisplayAttribute? display = null)
    {
        var socketType = typeof(ListInputSocket<>).MakeGenericType(type);
        var socket = (IListInputSocket)Activator.CreateInstance(socketType)!;
        socket.Name = name;
        if (socket is NodeItem nodeItem)
        {
            nodeItem.Display = display;
        }
        Items.Add(socket);
        return socket;
    }

    protected IListOutputSocket AddListOutput(string name, Type type, DisplayAttribute? display = null)
    {
        var socketType = typeof(ListOutputSocket<>).MakeGenericType(type);
        var socket = (IListOutputSocket)Activator.CreateInstance(socketType)!;
        socket.Name = name;
        if (socket is NodeItem nodeItem)
        {
            nodeItem.Display = display;
        }
        Items.Add(socket);
        return socket;
    }

    protected OutputSocket<T> AddOutput<T>(string name, T value, DisplayAttribute? display = null)
    {
        OutputSocket<T> socket = CreateOutput<T>(name, value, display);
        Items.Add(socket);
        return socket;
    }

    protected OutputSocket<T> AddOutput<T>(string name, DisplayAttribute? display = null)
    {
        OutputSocket<T> socket = CreateOutput<T>(name, display);
        Items.Add(socket);
        return socket;
    }

    protected NodeItem<T> AddProperty<T>(string name, DisplayAttribute? display = null)
    {
        NodeItem<T> socket = CreateProperty<T>(name, display);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> CreateInput<T>(string name, DisplayAttribute? display = null)
    {
        var adapter = new NodePropertyAdapter<T>(name);
        var socket = new DefaultInputSocket<T>();
        socket.SetPropertyAdapter(adapter);
        socket.Name = name;
        socket.Display = display;
        return socket;
    }

    protected IInputSocket CreateInput(string name, Type type, DisplayAttribute? display = null)
    {
        var adapter = Activator.CreateInstance(typeof(NodePropertyAdapter<>).MakeGenericType(type), name)!;
        var socket = (IDefaultInputSocket)Activator.CreateInstance(typeof(DefaultInputSocket<>).MakeGenericType(type))!;
        socket.SetPropertyAdapter(adapter);
        socket.Name = name;
        if (socket is NodeItem nodeItemSocket)
        {
            nodeItemSocket.Display = display;
        }
        return socket;
    }

    protected OutputSocket<T> CreateOutput<T>(string name, T value, DisplayAttribute? display = null)
    {
        return new OutputSocket<T>()
        {
            Name = name,
            Display = display,
            Value = value
        };
    }

    protected OutputSocket<T> CreateOutput<T>(string name, DisplayAttribute? display = null)
    {
        return new OutputSocket<T>()
        {
            Name = name,
            Display = display
        };
    }

    protected IOutputSocket CreateOutput(string name, Type type, DisplayAttribute? display = null)
    {
        var socket = (IOutputSocket)Activator.CreateInstance(typeof(OutputSocket<>).MakeGenericType(type))!;
        if (socket is NodeItem nodeItem)
        {
            nodeItem.Name = name;
            nodeItem.Display = display;
        }
        return socket;
    }

    protected NodeItem<T> CreateProperty<T>(string name, DisplayAttribute? display = null)
    {
        var adapter = new NodePropertyAdapter<T>(name);
        var socket = new DefaultNodeItem<T>();
        socket.SetProperty(adapter);
        socket.Name = name;
        socket.Display = display;
        return socket;
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
            foreach (JsonNode? item in itemsArray)
            {
                if (item is JsonObject itemObj
                    && itemObj.TryGetPropertyValue("Name", out var nameNode)
                    && nameNode is JsonValue nameValue
                    && nameValue.TryGetValue(out string? itemName))
                {
                    INodeItem? nodeItem = Items.FirstOrDefault(x => x.Name == itemName);
                    if (nodeItem is ICoreSerializable serializable)
                    {
                        CoreSerializer.PopulateFromJsonObject(serializable, itemObj);
                    }
                }
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Position), $"{Position.X},{Position.Y}");

        context.SetValue(nameof(Items), Items);
    }
}
