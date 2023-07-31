using Beutl.Language;
using Beutl.NodeTree.Nodes.Brushes;
using Beutl.NodeTree.Nodes.Effects;
using Beutl.NodeTree.Nodes.Geometry;
using Beutl.NodeTree.Nodes.Group;
using Beutl.NodeTree.Nodes.Transform;

namespace Beutl.NodeTree.Nodes;

public static class NodesRegistrar
{
    public static void RegisterAll()
    {
        NodeRegistry.RegisterNode<LayerInputNode>("Layer input");
        NodeRegistry.RegisterNode<LayerOutputNode>("Layer output");
        NodeRegistry.RegisterNode<GeometryShapeNode>("GeometryShape");

        NodeRegistry.RegisterNodes("Geometry")
            .Add<RectGeometryNode>(Strings.Rectangle)
            .Add<EllipseGeometryNode>(Strings.Ellipse)
            .Add<RoundedRectGeometryNode>(Strings.RoundedRect)
            .Register();

        NodeRegistry.RegisterNodes("Group")
            .Add<GroupInput>("Group Input")
            .Add<GroupOutput>("Group Output")
            .Add<GroupNode>("Group Node")
            .Register();

        NodeRegistry.RegisterNodes(Strings.ImageFilter)
            .Add<DropShadowNode>(Strings.DropShadow)
            .Register();

        NodeRegistry.RegisterNodes("Brush")
            .Add<ForegroundNode>("Set Foreground")
            .Add<SolidColorBrushNode>("Solid Color Brush")
            .Add<LinearGradientBrushNode>("Linear Gradient Brush")
            .Add<RadialGradientBrushNode>("Radial Gradient Brush")
            .Add<ConicGradientBrushNode>("Conic Gradient Brush")
            .Add<DrawableBrushNode>("Drawable Gradient Brush")
            .Register();

        NodeRegistry.RegisterNodes(Strings.Transform)
            .Add<MatrixTransformNode>(Strings.Transform)
            .Add<TranslateTransformNode>(Strings.Translate)
            .Add<RotationTransformNode>(Strings.Rotation)
            .Add<Rotation3DTransformNode>(Strings.Rotation3D)
            .Add<ScaleTransformNode>(Strings.Scale)
            .Add<SkewTransformNode>(Strings.Skew)
            .Register();

        NodeRegistry.RegisterNodes("Utilities")
            .Add<Utilities.SwitchNode>("Switch")
            .Add<Utilities.MeasureNode>("Measure")
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
