using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph.Nodes;

public static class NodesRegistrar
{
    public static void RegisterAll()
    {
        GraphNodeRegistry.RegisterNode<LayerInputNode>(NodeGraphStrings.GraphInputs, Colors.Crimson);
        GraphNodeRegistry.RegisterNode<OutputNode>(NodeGraphStrings.GraphOutput, Colors.Crimson);
        GraphNodeRegistry.RegisterNode<FilterEffectInputNode>(NodeGraphStrings.EffectInput, Colors.Crimson);
        GraphNodeRegistry.RegisterNode<GeometryShapeNode>(GraphicsStrings.GeometryShape, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<TextNode>(GraphicsStrings.TextBlock, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<ImageSourceNode>(GraphicsStrings.SourceImage, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<VideoSourceNode>(GraphicsStrings.SourceVideo, Colors.ForestGreen);
        GraphNodeRegistry.RegisterNode<TransformNode>(GraphicsStrings.Transform, Colors.MediumPurple);

        GraphNodeRegistry.RegisterNodes(NodeGraphStrings.Shapes, Colors.ForestGreen)
            .Add<RectGeometryNode>(GraphicsStrings.RectShape)
            .Add<EllipseGeometryNode>(GraphicsStrings.EllipseShape)
            .Add<RoundedRectGeometryNode>(GraphicsStrings.RoundedRectShape)
            .Register();

        GraphNodeRegistry.RegisterNodes(NodeGraphStrings.Groups, Colors.Gold)
            .Add<GroupInput>(NodeGraphStrings.GroupInputs)
            .Add<GroupOutput>(NodeGraphStrings.GroupOutputs)
            .Add<GroupNode>(NodeGraphStrings.Group)
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
                .Add<FilterEffectNode<GLSLScriptEffect>>(GraphicsStrings.GLSLScriptEffect)
                .Register())
            .Register();

        GraphNodeRegistry.RegisterNodes(GraphicsStrings.Brush, Colors.Orange)
            .Add<FactoryNode<SolidColorBrush>>(GraphicsStrings.SolidColorBrush)
            .Add<FactoryNode<LinearGradientBrush>>(GraphicsStrings.LinearGradientBrush)
            .Add<FactoryNode<ConicGradientBrush>>(GraphicsStrings.ConicGradientBrush)
            .Add<FactoryNode<RadialGradientBrush>>(GraphicsStrings.RadialGradientBrush)
            .Add<FactoryNode<PerlinNoiseBrush>>(GraphicsStrings.PerlinNoiseBrush)
            .Add<FactoryNode<DrawableBrush>>(GraphicsStrings.Drawable)
            .Register();

        GraphNodeRegistry.RegisterNodes(NodeGraphStrings.ToolsAndValues)
            .Add<Utilities.SwitchNode>(NodeGraphStrings.ConditionalSwitch)
            .Add<Utilities.MeasureNode>(NodeGraphStrings.MeasureBounds)
            .Add<Utilities.PreviewNode>(NodeGraphStrings.ImagePreview)
            .Add<Utilities.TimeNode>(NodeGraphStrings.TimelineTime)
            .Add<Utilities.ExpressionNode>(NodeGraphStrings.CSharpExpression)
            .AddGroup(NodeGraphStrings.TransformMatrices, o => o
                .Add<Utilities.TranslateMatrixNode>(NodeGraphStrings.TranslationMatrix)
                .Add<Utilities.RotationMatrixNode>(NodeGraphStrings.RotationMatrix)
                .Add<Utilities.Rotation3DMatrixNode>(NodeGraphStrings.Rotation3DMatrix)
                .Add<Utilities.ScaleMatrixNode>(NodeGraphStrings.ScaleMatrix)
                .Add<Utilities.SkewMatrixNode>(NodeGraphStrings.SkewMatrix)
                .Register())
            .AddGroup(NodeGraphStrings.RandomNumbers, o => o
                .Add<Utilities.RandomSingleNode>(NodeGraphStrings.RandomFloat32)
                .Add<Utilities.RandomDoubleNode>(NodeGraphStrings.RandomFloat64)
                .Add<Utilities.RandomInt32Node>(NodeGraphStrings.RandomInt32)
                .Add<Utilities.RandomInt64Node>(NodeGraphStrings.RandomInt64)
                .Register())
            .AddGroup(NodeGraphStrings.CoordinatesAndSizes, o => o
                .Add<Utilities.Struct.PointNode>(NodeGraphStrings.Point)
                .Add<Utilities.Struct.SizeNode>(NodeGraphStrings.Size)
                .Add<Utilities.Struct.RectNode>(NodeGraphStrings.Rectangle)
                .Add<Utilities.Struct.RelativePointNode>(NodeGraphStrings.RelativePoint)
                .Add<Utilities.Struct.RelativeRectNode>(NodeGraphStrings.RelativeRectangle)
                .Add<Utilities.Struct.PixelPointNode>(NodeGraphStrings.PixelPoint)
                .Add<Utilities.Struct.PixelSizeNode>(NodeGraphStrings.PixelSize)
                .Add<Utilities.Struct.PixelRectNode>(NodeGraphStrings.PixelRectangle)
                .Register())
            .Register();
    }
}
