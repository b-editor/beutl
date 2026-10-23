using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph;

public interface INodeMember : ICoreObject, IHierarchical, INotifyEdited
{
    IProperty<DisplayAttribute?> Display { get; }

    IPropertyAdapter? Property { get; }

    Type? AssociatedType { get; }

    public event EventHandler? TopologyChanged;

    IItemValue CreateItemValue();
}
