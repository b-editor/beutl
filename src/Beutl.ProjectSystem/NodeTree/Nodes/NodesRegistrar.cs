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
            .Add<RotationNode>(Strings.Rotation)
            .Add<ScaleNode>(Strings.Scale)
            .Add<SkewNode>(Strings.Skew)
            .Register();

        NodeRegistry.RegisterNodes("Math")
            .Add<Mathematics.TranslateMatrixNode>("Translate")
            .Add<Mathematics.RotationMatrixNode>("Rotation")
            .Add<Mathematics.ScaleMatrixNode>("Scale")
            .Add<Mathematics.SkewMatrixNode>("Skew")
            .Register();
    }
}
