using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph;

[SuppressResourceClassGeneration]
public abstract class NodeMember : EngineObject
{
    protected NodeMember()
    {
        ScanProperties<NodeMember>();
        HideProperty(DisplayProperty);
        DisplayProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Display)));
    }

    [NotAutoSerialized]
    [NotTracked]
    public IProperty<DisplayAttribute?> DisplayProperty { get; } = new ValueOnlyProperty<DisplayAttribute?>(null) { AffectsRender = false };

    public DisplayAttribute? Display
    {
        get => DisplayProperty.Value;
        set => DisplayProperty.Value = value;
    }

    public event EventHandler? TopologyChanged;

    protected void RaiseTopologyChanged()
    {
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }
}

public class NodeMember<T> : NodeMember, INodeMember, IChildObjectSource
{
    public IPropertyAdapter<T>? Property { get; protected set; }

    public virtual Type? AssociatedType => typeof(T);

    IPropertyAdapter? INodeMember.Property => Property;

    // The adapter holds the port's value, which no registered property does.
    IEnumerable<object?> IChildObjectSource.GetChildObjects() => [.. Properties, Property];

    // Shared by DefaultInputPort<T>, DefaultNodeMember<T> and LayerInputPort<T>, which keep their value in an adapter.
    private protected NodePropertyAdapter<T>? PropertyAdapter => Property as NodePropertyAdapter<T>;

    private protected void SetPropertyAdapterCore(NodePropertyAdapter<T> property)
    {
        Property = property;
        property.Edited += OnPropertyAdapterEdited;
    }

    private void OnPropertyAdapterEdited(object? sender, EventArgs e)
    {
        RaiseEdited();
    }

    public IItemValue CreateItemValue()
    {
        var value = new ItemValue<T>();
        ItemValueHelper.RegisterDefaultReceiver(value, this);
        return value;
    }
}
