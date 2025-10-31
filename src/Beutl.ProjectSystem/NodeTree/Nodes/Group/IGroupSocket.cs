namespace Beutl.NodeTree.Nodes.Group;

public interface IGroupSocket : ISocket
{
    string? AssociatedPropertyName { get; set; }

    Type? AssociatedPropertyType { get; set; }
}
