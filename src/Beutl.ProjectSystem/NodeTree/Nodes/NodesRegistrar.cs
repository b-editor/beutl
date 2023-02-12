using Beutl.Language;
using Beutl.NodeTree.Nodes.Brushes;
using Beutl.NodeTree.Nodes.Transform;

namespace Beutl.NodeTree.Nodes;

public static class NodesRegistrar
{
    public static void RegisterAll()
    {
        NodeRegistry.RegisterNode<RectNode>(Strings.Rectangle);
        NodeRegistry.RegisterNode<LayerOutputNode>("Layer output");

        NodeRegistry.RegisterNodes("Brush")
            .Add<ForegroundNode>("Set Foreground")
            .Add<LinearGradientBrushNode>("Linear Gradient Brush")
            .Register();
        
        NodeRegistry.RegisterNodes("Transform")
            .Add<TransformNode>(Strings.Transform)
            .Add<TranslateNode>(Strings.Translate)
            .Add<RotationNode>(Strings.Rotation)
            .Add<Rotation3DNode>(Strings.Rotation3D)
            .Add<ScaleNode>(Strings.Scale)
            .Add<SkewNode>(Strings.Skew)
            .Register();

        NodeRegistry.RegisterNodes("Utilities")
            .Add<Utilities.SwitchNode>("Switch")
            .AddGroup("Matrix", o => o
                .Add<Utilities.TranslateMatrixNode>("Translate")
                .Add<Utilities.RotationMatrixNode>("Rotation")
                .Add<Utilities.Rotation3DMatrixNode>("Rotation 3D")
                .Add<Utilities.ScaleMatrixNode>("Scale")
                .Add<Utilities.SkewMatrixNode>("Skew")
                .Register())
            .AddGroup("Random", o => o
                .Add<Utilities.RandomSingleNode>("Random Float")
                .Add<Utilities.RandomDoubleNode>("Random Double")
                .Add<Utilities.RandomInt32Node>("Random 32-bit signed integer")
                .Add<Utilities.RandomInt64Node>("Random 64-bit signed integer")
                .Register())
            .AddGroup("Struct", o => o
                .Add<Utilities.Struct.PointNode>("Point")
                .Add<Utilities.Struct.SizeNode>("Size")
                .Add<Utilities.Struct.RectNode>("Rect")
                .Add<Utilities.Struct.RelativePointNode>("Relative Point")
                .Add<Utilities.Struct.PixelPointNode>("Pixel Point")
                .Add<Utilities.Struct.PixelSizeNode>("Pixel Size")
                .Add<Utilities.Struct.PixelRectNode>("Pixel Rect")
                .Register())
            .Register();
    }
}
