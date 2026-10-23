using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

[FallbackType(typeof(FallbackGraphNode))]
public abstract partial class GraphNode : EngineObject
{
    private readonly ListProperty<INestedInputPort> _nestedInputPorts = new() { AffectsRender = false };
    private readonly NestedInputPortManager _nestedPortManager;
    private IInputPort[]? _connectedInputs;

    public GraphNode()
    {
        _nestedPortManager = new(this);
        // Each use of the ports brings them up to date first, as the NestedInputPorts accessor did.
        _nestedInputPorts.BeforeAccess = () => _nestedPortManager.EnsureSynchronized();
        ScanProperties<GraphNode>();
        HideProperties(IsExpanded, Position, Items, NestedInputPorts);

        Items.Attached += OnItemAttached;
        Items.Detached += OnItemDetached;
        Items.CollectionChanged += (_, _) => _nestedPortManager.Synchronize();
        _nestedInputPorts.Attached += port =>
        {
            OnItemAttached(port);
            if (!_nestedPortManager.IsSynchronizing) _nestedPortManager.Bind(port);
        };
        _nestedInputPorts.Detached += port =>
        {
            OnItemDetached(port);
            port.Unbind();
        };
        _nestedInputPorts.CollectionChanged += (_, _) =>
        {
            if (!_nestedPortManager.IsSynchronizing) NotifyNestedInputPortsChanged();
        };
    }

    private void OnItemDetached(INodeMember obj)
    {
        _connectedInputs = null;
        obj.TopologyChanged -= OnItemTopologyChanged;
        obj.Edited -= OnItemEdited;
    }

    private void OnItemAttached(INodeMember obj)
    {
        _connectedInputs = null;
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

    [SuppressResourceClassGeneration]
    public IProperty<bool> IsExpanded { get; } = new ValueOnlyProperty<bool>(true) { AffectsRender = false };

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<(double X, double Y)> Position { get; } = new ValueOnlyProperty<(double X, double Y)>((0, 0)) { AffectsRender = false };

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IListProperty<INodeMember> Items { get; } = new ListProperty<INodeMember> { AffectsRender = false };

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IListProperty<INestedInputPort> NestedInputPorts => _nestedInputPorts;

    public event EventHandler? NestedInputPortsChanged;

    internal void NotifyNestedInputPortsChanged()
    {
        _connectedInputs = null;
        NestedInputPortsChanged?.Invoke(this, EventArgs.Empty);
        RaiseTopologyChanged();
    }

    internal void RemoveNestedInputPorts(IEnumerable<INestedInputPort> ports) => _nestedInputPorts.RemoveAll(ports);

    /// <summary>Root members come first, retaining the indices used by group-node resources.</summary>
    public IEnumerable<INodeMember> EnumerateMembers()
    {
        _nestedPortManager.EnsureSynchronized();
        return Items.Concat<INodeMember>(_nestedInputPorts);
    }

    internal IInputPort[] GetConnectedInputs()
    {
        _nestedPortManager.EnsureSynchronized();
        return _connectedInputs ??= Items.Concat<INodeMember>(_nestedInputPorts)
            .OfType<IInputPort>().Where(p => !p.Connection.Value.IsNull).ToArray();
    }

    // Check binding and ancestor overrides without consulting other connections. Alias indexing
    // uses this first so an independently rejected path cannot block an otherwise usable writer.
    internal bool IsInputTargetAvailable(IInputPort input)
    {
        _nestedPortManager.EnsureSynchronized();
        if (input is INestedInputPort nestedInput
            && (nestedInput.Property == null || _nestedPortManager.HasOverridingAncestor(nestedInput))) return false;
        return input.Property?.GetEngineProperty() is null or IExpressionProperty;
    }

    /// <summary>Object inputs and their descendant inputs cannot both supply a value.</summary>
    public bool CanConnectInput(IInputPort input)
    {
        if (!IsInputTargetAvailable(input)) return false;
        IProperty? property = input.Property?.GetEngineProperty();
        if (property != null && this.FindHierarchicalParent<GraphModel>() is { } graph
            && graph.HasAliasedInput(input, property)) return false;
        foreach (IInputPort other in GetConnectedInputs())
        {
            if (other == input) continue;
            // Distinct paths can still drive the same expression when objects are shared.
            if (property != null && ReferenceEquals(other.Property?.GetEngineProperty(), property)
                && IsInputTargetAvailable(other)) return false;
            if (input is INestedInputPort nested)
            {
                if (ConflictsWithNestedInput(nested, other)) return false;
            }
            else if (other is INestedInputPort descendant && descendant.RootMember.Id == input.Id.Value)
            {
                return false;
            }
        }
        return true;
    }

    // other drives the nested port's root member itself, or a nested port of the same root whose path
    // contains or lies inside the nested port's.
    private static bool ConflictsWithNestedInput(INestedInputPort nested, IInputPort other)
    {
        return other.Id.Value == nested.RootMember.Id
               || other is INestedInputPort child && child.RootMember.Id == nested.RootMember.Id
               && (IsPathPrefix(child.PropertyPath, nested.PropertyPath)
                   || IsPathPrefix(nested.PropertyPath, child.PropertyPath));
    }

    // Re-adds the ports a node creates at run time from their serialized "Items".
    internal void RestoreDynamicPorts<TPort>(ICoreSerializationContext context)
        where TPort : INodeMember
    {
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(TPort)) is TPort port)
                {
                    Items.Add(port);
                }
            }
        }
    }

    internal static bool IsPathPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> path)
        => prefix.Count <= path.Count && prefix.SequenceEqual(path.Take(prefix.Count));

    public event EventHandler? TopologyChanged;

    protected void RaiseTopologyChanged()
    {
        _connectedInputs = null;
        if (!_nestedPortManager.IsSynchronizing) TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

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

    protected ListInputPort<T> AddListInput<T>(string name, DisplayAttribute? display = null)
    {
        var port = new ListInputPort<T>() { Name = { Value = name }, Display = { Value = display } };
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
        var monitor = new NodeMonitor<T>() { Name = { Value = name }, Display = { Value = display }, ContentKind = contentKind };
        Items.Add(monitor);
        return monitor;
    }

    protected NodeMonitor<string?> AddTextMonitor(string name, DisplayAttribute? display = null)
    {
        return AddMonitor<string?>(name, NodeMonitorContentKind.Text, display);
    }

    protected NodeMonitor<Ref<Bitmap>?> AddImageMonitor(string name, DisplayAttribute? display = null)
    {
        return AddMonitor<Ref<Bitmap>?>(name, NodeMonitorContentKind.Image, display);
    }

    /// <summary>Edits a text input in a multi-line field, as long prompts and scripts need.</summary>
    protected static void UseMultilineEditor(InputPort<string> port)
    {
        if (port.Property is NodePropertyAdapter<string> adapter)
            adapter.Attributes = [.. adapter.Attributes, new DataTypeAttribute(DataType.MultilineText)];
    }

    protected InputPort<T> CreateInput<T>(string name, DisplayAttribute? display = null)
    {
        var adapter = new NodePropertyAdapter<T>(name);
        var port = new DefaultInputPort<T>();
        port.SetPropertyAdapter(adapter);
        port.Name.Value = name;
        port.Display.Value = display;
        return port;
    }

    protected IInputPort CreateInput(string name, Type type, DisplayAttribute? display = null)
    {
        var adapter = Activator.CreateInstance(typeof(NodePropertyAdapter<>).MakeGenericType(type), name)!;
        var port = (IDefaultInputPort)Activator.CreateInstance(typeof(DefaultInputPort<>).MakeGenericType(type))!;
        port.SetPropertyAdapter(adapter);
        port.Name.Value = name;
        if (port is NodeMember nodeMember)
        {
            nodeMember.Display.Value = display;
        }

        return port;
    }

    protected OutputPort<T> CreateOutput<T>(string name, DisplayAttribute? display = null)
    {
        return new OutputPort<T>() { Name = { Value = name }, Display = { Value = display } };
    }

    protected IOutputPort CreateOutput(string name, Type type, DisplayAttribute? display = null)
    {
        var port = (IOutputPort)Activator.CreateInstance(typeof(OutputPort<>).MakeGenericType(type))!;
        if (port is NodeMember nodeMember)
        {
            nodeMember.Name.Value = name;
            nodeMember.Display.Value = display;
        }

        return port;
    }

    protected NodeMember<T> CreateProperty<T>(string name, DisplayAttribute? display = null)
    {
        var adapter = new NodePropertyAdapter<T>(name);
        var port = new DefaultNodeMember<T>();
        port.SetProperty(adapter);
        port.Name.Value = name;
        port.Display.Value = display;
        return port;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<string>(nameof(Position)) is { } posStr
            && TryParsePosition(posStr, out var position))
        {
            Position.Value = position;
        }

        // A missing plugin's node reads what every node has. Its ports stay unknown members and are written back.
        if (this is FallbackGraphNode fallback)
        {
            fallback.MarkMetadataRead();
            return;
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
                    INodeMember? nodeMember = Items.FirstOrDefault(x => x.Name.Value == itemName);
                    if (nodeMember is ICoreSerializable serializable)
                    {
                        CoreSerializer.PopulateFromJsonObject(serializable, itemObj);
                    }
                }
            }
        }

        if (context.GetValue<INestedInputPort[]>(nameof(NestedInputPorts)) is { } nestedPorts)
            _nestedInputPorts.Replace(nestedPorts);
        _nestedPortManager.Synchronize();
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        _nestedPortManager.EnsureSynchronized(force: true);
        base.Serialize(context);
        context.SetValue(nameof(Position), string.Create(CultureInfo.InvariantCulture, $"{Position.Value.X:R},{Position.Value.Y:R}"));
        // A fallback with saved ports writes them back as they were read.
        if (this is FallbackGraphNode { Json: not null }) return;

        context.SetValue(nameof(Items), Items);
        if (_nestedInputPorts.Count > 0)
            context.SetValue(nameof(NestedInputPorts), _nestedInputPorts);
    }

    internal static bool TryParsePosition(string value, out (double X, double Y) position)
    {
        position = default;
        ReadOnlySpan<char> span = value.AsSpan();
        int separator = span.IndexOf(',');
        // Older invariant-culture saves use the same two-field format. A comma-decimal save
        // can have more fields, but its original coordinates cannot be inferred reliably.
        if (separator < 0 || span[(separator + 1)..].Contains(',')) return false;

        if (!double.TryParse(span[..separator], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            || !double.TryParse(span[(separator + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
            || !double.IsFinite(x) || !double.IsFinite(y)) return false;

        position = (x, y);
        return true;
    }

    public partial class Resource
    {
        public int SlotIndex { get; internal set; }
        public IItemValue[] ItemValues { get; internal set; } = [];
        public INodeMember[] Members { get; internal set; } = [];
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
