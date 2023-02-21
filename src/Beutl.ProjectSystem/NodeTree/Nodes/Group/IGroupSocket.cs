namespace Beutl.NodeTree.Nodes.Group;

public interface IGroupSocket : ISocket
{
    CoreProperty? AssociatedProperty { get; set; }
}
