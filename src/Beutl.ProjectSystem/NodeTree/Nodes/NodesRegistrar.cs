using Beutl.Language;
using Beutl.NodeTree.Nodes.Transform;

namespace Beutl.NodeTree.Nodes;

public static class NodesRegistrar
{
    public static void RegisterAll()
    {
        NodeRegistry.RegisterNode<RectNode>(Strings.Rectangle);
        NodeRegistry.RegisterNode<LayerOutputNode>("Layer output");

        NodeRegistry.RegisterNodes("Transform")
            .Add<TransformNode>(Strings.Transform)
            .Add<TranslateNode>(Strings.Translate)
            .Register();

        NodeRegistry.RegisterNodes("Math")
            .Add<Mathematics.TranslateMatrixNode>("Translate")
            .Add<Mathematics.RotationMatrixNode>("Rotation")
            .Register();
    }
}
