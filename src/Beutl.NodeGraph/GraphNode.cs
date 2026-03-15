using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.NodeGraph;

public abstract partial class GraphNode : EngineObject
{
    public static readonly CoreProperty<bool> IsExpandedProperty;
    public static readonly CoreProperty<(double X, double Y)> PositionProperty;
    public static readonly CoreProperty<ICoreList<INodeMember>> ItemsProperty;
    private readonly HierarchicalList<INodeMember> _items;
    private (double X, double Y) _position;

    static GraphNode()
    {
        IsExpandedProperty = ConfigureProperty<bool, GraphNode>(nameof(IsExpanded))
            .DefaultValue(true)
            .Register();

        PositionProperty = ConfigureProperty<(double X, double Y), GraphNode>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue((0, 0))
            .Register();

        ItemsProperty = ConfigureProperty<ICoreList<INodeMember>, GraphNode>(nameof(Items))
            .Accessor(o => o.Items, (o, v) => o._items.Replace(v))
            .Register();
    }

    public GraphNode()
    {
        _items = new(this);

        _items.Attached += OnItemAttached;
        _items.Detached += OnItemDetached;
    }

    private void OnItemDetached(INodeMember obj)
    {
        obj.TopologyChanged -= OnItemTopologyChanged;
        obj.Edited -= OnItemEdited;
    }

    private void OnItemAttached(INodeMember obj)
    {
        obj.TopologyChanged += OnItemTopologyChanged;
        obj.Edited += OnItemEdited;
    }

    private void OnItemEdited(object? sender, EventArgs e)
    {
        RaiseEdited();
    }

    private void OnItemTopologyChanged(object? sender, EventArgs e)
    {
        RaiseTopologyChanged();
    }

    [NotAutoSerialized] public ICoreList<INodeMember> Items => _items;

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

    protected void RaiseTopologyChanged()
    {
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    // TODO: AddInput, AddOutput, AddPropertyに変更する
    protected InputPort<T> AddInput<T>(EngineObject obj, IProperty<T> property)
    {
        var port = new EnginePropertyBackedInputPort<T>(obj, property);
        Items.Add(port);
        return port;
    }

    protected IInputPort AddInput(EngineObject obj, IProperty property)
    {
        var type = typeof(EnginePropertyBackedInputPort<>).MakeGenericType(property.ValueType);
        var port = (IInputPort)Activator.CreateInstance(type, obj, property)!;
        Items.Add(port);
        return port;
    }

    protected InputPort<T> AddInput<T>(string name, DisplayAttribute? display = null)
    {
        InputPort<T> port = CreateInput<T>(name, display);
        Items.Add(port);
        return port;
    }

    protected IInputPort AddInput(string name, Type type, DisplayAttribute? display = null)
    {
        IInputPort port = CreateInput(name, type, display);
        Items.Add(port);
        return port;
    }

    protected ListInputPort<T> AddListInput<T>(string name, DisplayAttribute? display = null)
    {
        var port = new ListInputPort<T>() { Name = name, Display = display };
        Items.Add(port);
        return port;
    }

    protected ListOutputPort<T> AddListOutput<T>(string name, DisplayAttribute? display = null)
    {
        var port = new ListOutputPort<T>() { Name = name, Display = display };
        Items.Add(port);
        return port;
    }

    protected IListInputPort AddListInput(string name, Type type, DisplayAttribute? display = null)
    {
        var portType = typeof(ListInputPort<>).MakeGenericType(type);
        var port = (IListInputPort)Activator.CreateInstance(portType)!;
        port.Name = name;
        if (port is NodeMember nodeMember)
        {
            nodeMember.Display = display;
        }

        Items.Add(port);
        return port;
    }

    protected IListOutputPort AddListOutput(string name, Type type, DisplayAttribute? display = null)
    {
        var portType = typeof(ListOutputPort<>).MakeGenericType(type);
        var port = (IListOutputPort)Activator.CreateInstance(portType)!;
        port.Name = name;
        if (port is NodeMember nodeMember)
        {
            nodeMember.Display = display;
        }

        Items.Add(port);
        return port;
    }

    protected OutputPort<T> AddOutput<T>(string name, DisplayAttribute? display = null)
    {
        OutputPort<T> port = CreateOutput<T>(name, display);
        Items.Add(port);
        return port;
    }

    protected NodeMember<T> AddProperty<T>(string name, DisplayAttribute? display = null)
    {
        NodeMember<T> port = CreateProperty<T>(name, display);
        Items.Add(port);
        return port;
    }

    protected NodeMonitor<T> AddMonitor<T>(string name,
        NodeMonitorContentKind contentKind = NodeMonitorContentKind.Text,
        DisplayAttribute? display = null)
    {
        var monitor = new NodeMonitor<T>() { Name = name, Display = display, ContentKind = contentKind };
        Items.Add(monitor);
        return monitor;
    }

    protected NodeMonitor<string?> AddTextMonitor(string name, DisplayAttribute? display = null)
    {
        return AddMonitor<string?>(name, NodeMonitorContentKind.Text, display);
    }

    protected NodeMonitor<Ref<IBitmap>?> AddImageMonitor(string name, DisplayAttribute? display = null)
    {
        return AddMonitor<Ref<IBitmap>?>(name, NodeMonitorContentKind.Image, display);
    }

    protected InputPort<T> CreateInput<T>(string name, DisplayAttribute? display = null)
    {
        var adapter = new NodePropertyAdapter<T>(name);
        var port = new DefaultInputPort<T>();
        port.SetPropertyAdapter(adapter);
        port.Name = name;
        port.Display = display;
        return port;
    }

    protected IInputPort CreateInput(string name, Type type, DisplayAttribute? display = null)
    {
        var adapter = Activator.CreateInstance(typeof(NodePropertyAdapter<>).MakeGenericType(type), name)!;
        var port = (IDefaultInputPort)Activator.CreateInstance(typeof(DefaultInputPort<>).MakeGenericType(type))!;
        port.SetPropertyAdapter(adapter);
        port.Name = name;
        if (port is NodeMember nodeMember)
        {
            nodeMember.Display = display;
        }

        return port;
    }

    protected OutputPort<T> CreateOutput<T>(string name, DisplayAttribute? display = null)
    {
        return new OutputPort<T>() { Name = name, Display = display };
    }

    protected IOutputPort CreateOutput(string name, Type type, DisplayAttribute? display = null)
    {
        var port = (IOutputPort)Activator.CreateInstance(typeof(OutputPort<>).MakeGenericType(type))!;
        if (port is NodeMember nodeMember)
        {
            nodeMember.Name = name;
            nodeMember.Display = display;
        }

        return port;
    }

    protected NodeMember<T> CreateProperty<T>(string name, DisplayAttribute? display = null)
    {
        var adapter = new NodePropertyAdapter<T>(name);
        var port = new DefaultNodeMember<T>();
        port.SetProperty(adapter);
        port.Name = name;
        port.Display = display;
        return port;
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
                    INodeMember? nodeMember = Items.FirstOrDefault(x => x.Name == itemName);
                    if (nodeMember is ICoreSerializable serializable)
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

    public partial class Resource
    {
        public int SlotIndex { get; internal set; }
        public IItemValue[] ItemValues { get; internal set; } = [];
        public IRenderer? Renderer { get; internal set; }
        public Dictionary<INodeMember, int> ItemIndexMap { get; set; } = new();

        public virtual void Initialize(GraphCompositionContext context)
        {
        }

        public virtual void Uninitialize()
        {
        }

        public virtual void BindNodePortValues()
        {
        }

        public virtual void Update(GraphCompositionContext context)
        {
        }
    }
}
