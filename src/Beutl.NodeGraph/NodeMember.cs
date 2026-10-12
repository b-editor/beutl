using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph;

[SuppressResourceClassGeneration]
public abstract class NodeMember : EngineObject
{
    public static readonly CoreProperty<DisplayAttribute?> DisplayProperty;
    private DisplayAttribute? _display;

    static NodeMember()
    {
        DisplayProperty = ConfigureProperty<DisplayAttribute?, NodeMember>(nameof(Display))
            .Accessor(o => o.Display, (o, v) => o.Display = v)
            .Register();
    }

    [NotAutoSerialized]
    [NotTracked]
    public DisplayAttribute? Display
    {
        get => _display;
        set => SetAndRaise(DisplayProperty, ref _display, value);
    }

    public event EventHandler? TopologyChanged;

    protected void RaiseTopologyChanged()
    {
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }
}

public class NodeMember<T> : NodeMember, INodeMember
{
    public IPropertyAdapter<T>? Property { get; protected set; }

    public virtual Type? AssociatedType => typeof(T);

    IPropertyAdapter? INodeMember.Property => Property;

    public IItemValue CreateItemValue()
    {
        var value = new ItemValue<T>();
        ItemValueHelper.RegisterDefaultReceiver(value, this);
        return value;
    }
}
