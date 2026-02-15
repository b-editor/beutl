using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree;

public abstract partial class NodeItem : EngineObject
{
    public static readonly CoreProperty<DisplayAttribute?> DisplayProperty;
    private DisplayAttribute? _display;

    static NodeItem()
    {
        DisplayProperty = ConfigureProperty<DisplayAttribute?, NodeItem>(nameof(Display))
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

public partial class NodeItem<T> : NodeItem, INodeItem
{
    public IPropertyAdapter<T>? Property { get; protected set; }

    public virtual Type? AssociatedType => typeof(T);

    IPropertyAdapter? INodeItem.Property => Property;

    public IItemValue CreateItemValue()
    {
        var value = new ItemValue<T>();
        ItemValueHelper.RegisterDefaultReceiver(value, this);
        return value;
    }
}
