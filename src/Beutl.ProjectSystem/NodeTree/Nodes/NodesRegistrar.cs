using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeTree.Nodes.Effects;
using Beutl.NodeTree.Nodes.Geometry;
using Beutl.NodeTree.Nodes.Group;

namespace Beutl.NodeTree.Nodes;

public static class NodesRegistrar
{
    public static void RegisterAll()
    {
        NodeRegistry.RegisterNode<LayerInputNode>("Layer input", Colors.Crimson);
        NodeRegistry.RegisterNode<OutputNode>("Layer output", Colors.Crimson);
        NodeRegistry.RegisterNode<GeometryShapeNode>(Strings.GeometryShape, Colors.ForestGreen);
        NodeRegistry.RegisterNode<TextNode>(Strings.Text, Colors.ForestGreen);
        NodeRegistry.RegisterNode<TransformNode>(Strings.Transform, Colors.MediumPurple);

        NodeRegistry.RegisterNodes("Geometry", Colors.ForestGreen)
            .Add<RectGeometryNode>(Strings.Rectangle)
            .Add<EllipseGeometryNode>(Strings.Ellipse)
            .Add<RoundedRectGeometryNode>(Strings.RoundedRect)
            .Register();

        NodeRegistry.RegisterNodes("Group", Colors.Gold)
            .Add<GroupInput>("Group Input")
            .Add<GroupOutput>("Group Output")
            .Add<GroupNode>("Group Node")
            .Register();

        NodeRegistry.RegisterNodes(Strings.FilterEffect, Colors.DodgerBlue)
            .Add<FilterEffectNode<Blur>>(Strings.Blur)
            .Add<FilterEffectNode<DropShadow>>(Strings.DropShadow)
            .Add<FilterEffectNode<InnerShadow>>(Strings.InnerShadow)
            .Add<FilterEffectNode<FlatShadow>>(Strings.FlatShadow)
            .Add<FilterEffectNode<StrokeEffect>>(Strings.StrokeEffect)
            .Add<FilterEffectNode<Clipping>>(Strings.Clipping)
            .Add<FilterEffectNode<Dilate>>(Strings.Dilate)
            .Add<FilterEffectNode<Erode>>(Strings.Erode)
            .Add<FilterEffectNode<HighContrast>>(Strings.HighContrast)
            .Add<FilterEffectNode<HueRotate>>(Strings.HueRotate)
            .Add<FilterEffectNode<Lighting>>(Strings.Lighting)
            .Add<FilterEffectNode<LumaColor>>(Strings.LumaColor)
            .Add<FilterEffectNode<Saturate>>(Strings.Saturate)
            .Add<FilterEffectNode<Threshold>>(Strings.Threshold)
            .Add<FilterEffectNode<Brightness>>(Strings.Brightness)
            .Add<FilterEffectNode<Gamma>>(Strings.Gamma)
            .Add<FilterEffectNode<ColorGrading>>(Strings.ColorGrading)
            .Add<FilterEffectNode<Curves>>(Strings.Curves)
            .Add<FilterEffectNode<Invert>>(Strings.Invert)
            .Add<FilterEffectNode<LutEffect>>(Strings.LUT_Cube_File)
            .Add<FilterEffectNode<BlendEffect>>(Strings.BlendEffect)
            .Add<FilterEffectNode<Negaposi>>(Strings.Negaposi)
            .Add<FilterEffectNode<ChromaKey>>(Strings.ChromaKey)
            .Add<FilterEffectNode<ColorKey>>(Strings.ColorKey)
            .Add<FilterEffectNode<SplitEffect>>(Strings.SplitEquallyEffect)
            .Add<FilterEffectNode<PartsSplitEffect>>(Strings.SplitByPartsEffect)
            .Add<FilterEffectNode<TransformEffect>>(Strings.Transform)
            .Add<FilterEffectNode<MosaicEffect>>(Strings.Mosaic)
            .Add<FilterEffectNode<ColorShift>>(Strings.ColorShift)
            .Add<FilterEffectNode<ShakeEffect>>(Strings.ShakeEffect)
            .Add<FilterEffectNode<DisplacementMapEffect>>(Strings.DisplacementMap)
            .Add<FilterEffectNode<PathFollowEffect>>(Strings.PathFollowEffect)
            .Add<FilterEffectNode<LayerEffect>>(Strings.Layer)
            .AddGroup(Strings.Script, o => o
                .Add<FilterEffectNode<CSharpScriptEffect>>(Strings.CSharpScriptEffect)
                .Add<FilterEffectNode<SKSLScriptEffect>>(Strings.SKSLScriptEffect)
                .Add<FilterEffectNode<GLSLScriptEffect>>(Strings.GLSLScriptEffect))
            .AddGroup("OpenCV", o => o
                .Add<FilterEffectNode<Graphics.Effects.OpenCv.Blur>>("CvBlur")
                .Add<FilterEffectNode<Graphics.Effects.OpenCv.GaussianBlur>>("CvGaussianBlur")
                .Add<FilterEffectNode<Graphics.Effects.OpenCv.MedianBlur>>("CvMedianBlur"))
            .Register();

        NodeRegistry.RegisterNodes(Strings.Brush, Colors.Orange)
            .Add<FactoryNode<SolidColorBrush>>(Strings.Brush_Solid)
            .Add<FactoryNode<LinearGradientBrush>>(Strings.Brush_LinearGradient)
            .Add<FactoryNode<ConicGradientBrush>>(Strings.Brush_ConicalGradient)
            .Add<FactoryNode<RadialGradientBrush>>(Strings.Brush_RadialGradient)
            .Add<FactoryNode<PerlinNoiseBrush>>(Strings.Brush_PerlinNoise)
            .Add<FactoryNode<DrawableBrush>>(Strings.Drawable)
            .Register();

        NodeRegistry.RegisterNodes("Utilities")
            .Add<Utilities.SwitchNode>("Switch")
            .Add<Utilities.MeasureNode>("Measure")
            .Add<Utilities.PreviewNode>("Preview")
            .Add<Utilities.TimeNode>("Time")
            .Add<Utilities.ExpressionNode>("Expression")
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
