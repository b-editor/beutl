using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>A port a node's current settings call for.</summary>
/// <param name="Name">The serialized identifier. A port survives a change while its name and type stay.</param>
/// <param name="Display">The label; the name is identical across languages when it comes from a shader.</param>
/// <param name="Default">The value a new input starts with.</param>
internal sealed record ShaderPortSpec(string Name, Type Type, bool IsOutput, DisplayAttribute Display, object? Default = null);

/// <summary>A node whose ports follow its settings, in the order outputs, anchor, inputs, other settings.</summary>
public abstract partial class ShaderDynamicNode : GraphNode
{
    private readonly HashSet<INodeMember> _fixedPorts = new(ReferenceEqualityComparer.Instance);
    private INodeMember? _inputAnchor;
    private bool _restoring;

    /// <summary>Declares the ports present now as fixed. Dynamic inputs follow <paramref name="inputAnchor"/>.</summary>
    protected void SealFixedPorts(INodeMember? inputAnchor = null)
    {
        _fixedPorts.UnionWith(Items);
        _inputAnchor = inputAnchor;
    }

    /// <summary>Whether saved values are being read, which says nothing new about the ports.</summary>
    private protected bool IsRestoring => _restoring;

    private protected IEnumerable<INodeMember> DynamicPorts => Items.Where(item => !_fixedPorts.Contains(item));

    /// <summary>Makes the dynamic ports match. An edit of the document only; history restores its own ports.</summary>
    private protected void SyncPorts(IReadOnlyList<ShaderPortSpec> desired)
    {
        if (_restoring || HistoryReplay.IsActive) return;

        bool changed = false;
        foreach (INodeMember item in DynamicPorts.ToArray())
        {
            if (desired.Any(spec => Matches(spec, item))) continue;
            RemovePort(item);
            changed = true;
        }

        int outputs = 0;
        int inputs = 0;
        foreach (ShaderPortSpec spec in desired)
        {
            int index = (spec.IsOutput ? outputs++ : outputs + AnchorOffset() + inputs++);
            int current = FindDynamicPort(spec);
            if (current < 0)
            {
                INodeMember port = Create(spec);
                if (!spec.IsOutput && spec.Default != null) ((IInputPort)port).Property?.SetValue(spec.Default);
                Items.Insert(index, port);
                changed = true;
            }
            else if (current != index)
            {
                Items.Move(current, index);
                changed = true;
            }
        }

        if (changed) RaiseTopologyChanged();
    }

    private int FindDynamicPort(ShaderPortSpec spec)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (!_fixedPorts.Contains(Items[i]) && Matches(spec, Items[i])) return i;
        }

        return -1;
    }

    // How many fixed members come before the first dynamic input.
    private int AnchorOffset()
    {
        if (_inputAnchor == null) return 0;
        int fixedBefore = 0;
        foreach (INodeMember item in Items)
        {
            if (!_fixedPorts.Contains(item)) continue;
            fixedBefore++;
            if (ReferenceEquals(item, _inputAnchor)) break;
        }
        return fixedBefore;
    }

    private static bool Matches(ShaderPortSpec spec, INodeMember item)
        => item.Name.Value == spec.Name && item.AssociatedType == spec.Type && item is IOutputPort == spec.IsOutput;

    private INodeMember Create(ShaderPortSpec spec)
        => spec.IsOutput ? CreateOutput(spec.Name, spec.Type, spec.Display) : CreateInput(spec.Name, spec.Type, spec.Display);

    private void RemovePort(INodeMember item)
    {
        this.FindHierarchicalParent<GraphModel>()?.DisconnectPort(item);
        Items.Remove(item);
    }

    /// <summary>The label for a saved dynamic port, which is not written with it.</summary>
    private protected virtual DisplayAttribute DisplayOf(string portName) => ShaderPortDisplays.Get(portName);

    private protected static ShaderPortSpec Spec<T>(string name, object? value = null)
        => new(name, typeof(T), false, ShaderPortDisplays.Get(name), value);

    /// <summary>Restores the ports the node had when saved, without compiling anything.</summary>
    public override void Deserialize(ICoreSerializationContext context)
    {
        _restoring = true;
        try
        {
            foreach (INodeMember item in DynamicPorts.ToArray())
                Items.Remove(item);

            int outputs = 0;
            int inputs = 0;
            if (context.GetValue<JsonArray>("Items") is { } saved)
            {
                foreach (JsonObject json in saved.OfType<JsonObject>())
                {
                    if (json["Name"] is not JsonValue nameValue || !nameValue.TryGetValue(out string? name)
                        || _fixedPorts.Any(port => port.Name.Value == name)
                        || CoreSerializer.DeserializeFromJsonObject(json, typeof(INodeMember)) is not INodeMember port
                        || port.AssociatedType is not { } type)
                    {
                        continue;
                    }

                    bool isOutput = port is IOutputPort;
                    // A port the serializer made has no adapter to hold an input's value, so make one that has.
                    INodeMember restored = Create(new(name, type, isOutput, DisplayOf(name)));
                    Items.Insert(isOutput ? outputs++ : outputs + AnchorOffset() + inputs++, restored);
                }
            }

            base.Deserialize(context);
        }
        finally
        {
            _restoring = false;
        }
    }

    public partial class Resource
    {
        private Dictionary<string, int>? _indices;

        private int IndexOf(string name)
        {
            if (_indices == null)
            {
                _indices = new(Members.Length, StringComparer.Ordinal);
                for (int i = 0; i < Members.Length; i++)
                    _indices.TryAdd(Members[i].Name.Value, i);
            }

            return _indices.GetValueOrDefault(name, -1);
        }

        private protected T? Get<T>(string name)
        {
            int index = IndexOf(name);
            return index >= 0 && ItemValues[index].GetBoxed() is T value ? value : default;
        }
    }
}
