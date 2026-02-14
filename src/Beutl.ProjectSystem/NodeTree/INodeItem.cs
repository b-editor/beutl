using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree;

public interface INodeItem : ICoreObject, IHierarchical, INotifyEdited
{
    DisplayAttribute? Display { get; }

    IPropertyAdapter? Property { get; }

    Type? AssociatedType { get; }

    public event EventHandler? TopologyChanged;

    IItemValue CreateItemValue();
}
