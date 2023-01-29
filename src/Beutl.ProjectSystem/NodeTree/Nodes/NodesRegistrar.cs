using Beutl.Language;

namespace Beutl.NodeTree.Nodes;

public static class NodesRegistrar
{
    public static void RegisterAll()
    {
        NodeRegistry.RegisterNode<RectNode>(Strings.Rectangle);
    }
}
