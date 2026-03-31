using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph.Nodes;

public static class NodesRegistrar
{
    public static void RegisterAll()
    {
        GraphNodeRegistry.RegisterNode<LayerInputNode>("Layer input", Colors.Crimson);
        GraphNodeRegistry.RegisterNode<OutputNode>("Layer output", Colors.Crimson);
        GraphNodeRegistry.RegisterNode<FilterEffectInputNode>("Effect Input", Colors.Crimson);
        GraphNodeRegistry.RegisterNode<GeometryShapeNode>(GraphicsStrings.GeometryShape, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<TextNode>(GraphicsStrings.TextBlock, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<ImageSourceNode>(GraphicsStrings.SourceImage, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<VideoSourceNode>(GraphicsStrings.SourceVideo, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<TransformNode>(GraphicsStrings.Transform, Colors.MediumPurple);

        GraphNodeRegistry.RegisterNodes("Geometry", Colors.ForestGreen)
            .Add<RectGeometryNode>(GraphicsStrings.RectShape)
            .Add<EllipseGeometryNode>(GraphicsStrings.EllipseShape)
            .Add<RoundedRectGeometryNode>(GraphicsStrings.RoundedRectShape)
            .Register();

        GraphNodeRegistry.RegisterNodes("Group", Colors.Gold)
            .Add<GroupInput>("Group Input")
            .Add<GroupOutput>("Group Output")
            .Add<GroupNode>("Group GraphNode")
            .Register();

        GraphNodeRegistry.RegisterNodes(GraphicsStrings.FilterEffect, Colors.DodgerBlue)
            .Add<FilterEffectNode<Blur>>(GraphicsStrings.Blur)
            .Add<FilterEffectNode<DropShadow>>(GraphicsStrings.DropShadow)
            .Add<FilterEffectNode<InnerShadow>>(GraphicsStrings.InnerShadow)
            .Add<FilterEffectNode<FlatShadow>>(GraphicsStrings.FlatShadow)
            .Add<FilterEffectNode<StrokeEffect>>(GraphicsStrings.Stroke)
            .Add<FilterEffectNode<Clipping>>(GraphicsStrings.Clipping)
            .Add<FilterEffectNode<Dilate>>(GraphicsStrings.Dilate)
            .Add<FilterEffectNode<Erode>>(GraphicsStrings.Erode)
            .Add<FilterEffectNode<HighContrast>>(GraphicsStrings.HighContrast)
            .Add<FilterEffectNode<HueRotate>>(GraphicsStrings.HueRotate)
            .Add<FilterEffectNode<Lighting>>(GraphicsStrings.Lighting)
            .Add<FilterEffectNode<LumaColor>>(GraphicsStrings.LumaColor)
            .Add<FilterEffectNode<Saturate>>(GraphicsStrings.Saturate)
            .Add<FilterEffectNode<Threshold>>(GraphicsStrings.Threshold)
            .Add<FilterEffectNode<Brightness>>(GraphicsStrings.Brightness)
            .Add<FilterEffectNode<Gamma>>(GraphicsStrings.Gamma)
            .Add<FilterEffectNode<ColorGrading>>(GraphicsStrings.ColorGrading)
            .Add<FilterEffectNode<Curves>>(GraphicsStrings.Curves)
            .Add<FilterEffectNode<Invert>>(GraphicsStrings.Invert)
            .Add<FilterEffectNode<LutEffect>>(GraphicsStrings.LutEffect)
            .Add<FilterEffectNode<BlendEffect>>(GraphicsStrings.BlendEffect)
            .Add<FilterEffectNode<Negaposi>>(GraphicsStrings.Negaposi)
            .Add<FilterEffectNode<ChromaKey>>(GraphicsStrings.ChromaKey)
            .Add<FilterEffectNode<ColorKey>>(GraphicsStrings.ColorKey)
            .Add<FilterEffectNode<SplitEffect>>(GraphicsStrings.SplitEffect)
            .Add<FilterEffectNode<PartsSplitEffect>>(GraphicsStrings.PartsSplitEffect)
            .Add<FilterEffectNode<TransformEffect>>(GraphicsStrings.Transform)
            .Add<FilterEffectNode<MosaicEffect>>(GraphicsStrings.MosaicEffect)
            .Add<FilterEffectNode<ColorShift>>(GraphicsStrings.ColorShift)
            .Add<FilterEffectNode<ShakeEffect>>(GraphicsStrings.ShakeEffect)
            .Add<FilterEffectNode<DisplacementMapEffect>>(GraphicsStrings.DisplacementMapEffect)
            .Add<FilterEffectNode<PathFollowEffect>>(GraphicsStrings.PathFollowEffect)
            .Add<FilterEffectNode<LayerEffect>>(GraphicsStrings.LayerEffect)
            .Add<FilterEffectNode<PixelSortEffect>>(GraphicsStrings.PixelSortEffect)
            .AddGroup(GraphicsStrings.Script, o => o
                .Add<FilterEffectNode<CSharpScriptEffect>>(GraphicsStrings.CSharpScriptEffect)
                .Add<FilterEffectNode<SKSLScriptEffect>>(GraphicsStrings.SKSLScriptEffect)
                .Add<FilterEffectNode<GLSLScriptEffect>>(GraphicsStrings.GLSLScriptEffect))
            .AddGroup(GraphicsStrings.Blur, o => o
                .Add<FilterEffectNode<Graphics.Effects.OpenCv.Blur>>("CvBlur")
                .Add<FilterEffectNode<Graphics.Effects.OpenCv.GaussianBlur>>("CvGaussianBlur")
                .Add<FilterEffectNode<Graphics.Effects.OpenCv.MedianBlur>>("CvMedianBlur"))
            .Register();

        GraphNodeRegistry.RegisterNodes(GraphicsStrings.Brush, Colors.Orange)
            .Add<FactoryNode<SolidColorBrush>>(GraphicsStrings.SolidColorBrush)
            .Add<FactoryNode<LinearGradientBrush>>(GraphicsStrings.LinearGradientBrush)
            .Add<FactoryNode<ConicGradientBrush>>(GraphicsStrings.ConicGradientBrush)
            .Add<FactoryNode<RadialGradientBrush>>(GraphicsStrings.RadialGradientBrush)
            .Add<FactoryNode<PerlinNoiseBrush>>(GraphicsStrings.PerlinNoiseBrush)
            .Add<FactoryNode<DrawableBrush>>(GraphicsStrings.Drawable)
            .Register();

        GraphNodeRegistry.RegisterNodes("Utilities")
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
                .Add<Utilities.Struct.RelativeRectNode>("Relative Rect")
                .Add<Utilities.Struct.PixelPointNode>("Pixel Point")
                .Add<Utilities.Struct.PixelSizeNode>("Pixel Size")
                .Add<Utilities.Struct.PixelRectNode>("Pixel Rect")
                .Register())
            .Register();
    }
}
