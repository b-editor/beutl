using Beutl.NodeTree.Nodes.Group;

namespace Beutl.NodeTree;

internal class NodeDisplayNameHelper
{
    public static string GetDisplayName(INodeItem item)
    {
        string? name = (item as CoreObject)?.Name;
        if (string.IsNullOrWhiteSpace(name) || name == "Unknown")
        {
            name = null;
        }

        if (item.Property is { } property)
        {
            return property.DisplayName;
        }
        else if (item is IGroupSocket { AssociatedProperty: { } asProperty })
        {
            CorePropertyMetadata metadata = asProperty.GetMetadata<CorePropertyMetadata>(asProperty.OwnerType);

            return name ?? metadata.DisplayAttribute?.GetName() ?? asProperty.Name;
        }
        else
        {
            return name ?? "Unknown";
        }
    }

    public static string GetDisplayName(CoreProperty property)
    {
        CorePropertyMetadata metadata = property.GetMetadata<CorePropertyMetadata>(property.OwnerType);

        return metadata.DisplayAttribute?.GetName() ?? property.Name;
    }
}
