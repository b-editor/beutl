using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeTree.Nodes.Transform;
using Beutl.Operation;
using Beutl.Services;

namespace Beutl.Operators;

public static class LibraryRegistrar
{
    public static void RegisterAll()
    {
        LibraryService.Current
            .AddMultiple(Strings.Ellipse, m => m
                .BindSourceOperator<Source.EllipseOperator>()
                .BindDrawable<Graphics.Shapes.EllipseShape>()
                .BindNode<NodeTree.Nodes.Geometry.EllipseGeometryNode>()
                .BindGeometry<EllipseGeometry>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Rectangle, m => m
                .BindSourceOperator<Source.RectOperator>()
                .BindDrawable<Graphics.Shapes.RectShape>()
                .BindNode<NodeTree.Nodes.Geometry.RectGeometryNode>()
                .BindGeometry<RectGeometry>()
            );

        LibraryService.Current
            .AddMultiple(Strings.RoundedRect, m => m
                .BindSourceOperator<Source.RoundedRectOperator>()
                .BindDrawable<Graphics.Shapes.RoundedRectShape>()
                .BindNode<NodeTree.Nodes.Geometry.RoundedRectGeometryNode>()
                .BindGeometry<RoundedRectGeometry>()
            );

        LibraryService.Current
            .AddMultiple(Strings.GeometryShape, m => m
                .BindSourceOperator<Source.GeometryOperator>()
                .BindDrawable<Graphics.Shapes.GeometryShape>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Text, m => m
                .BindSourceOperator<Source.TextBlockOperator>()
                .BindDrawable<Graphics.Shapes.TextBlock>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Video, m => m
                .BindSourceOperator<Source.SourceVideoOperator>()
                .BindDrawable<SourceVideo>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Image, m => m
                .BindSourceOperator<Source.SourceImageOperator>()
                .BindDrawable<SourceImage>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Backdrop, m => m
                .BindSourceOperator<Source.SourceBackdropOperator>()
                .BindDrawable<SourceBackdrop>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Sound, m => m
                .BindSourceOperator<Source.SourceSoundOperator>()
                .BindSound<SourceSound>()
            );

        LibraryService.Current
            .AddMultiple("下位N個の要素を取得", m => m
                .BindSourceOperator<TakeAfterOperator>()
            );

        LibraryService.Current
            .AddMultiple("デコレーター", m => m
                .BindSourceOperator<DecorateOperator>()
            );

        LibraryService.Current
            .AddMultiple("グループ", m => m
                .BindSourceOperator<GroupOperator>()
            );

        LibraryService.Current
            .RegisterGroup(Strings.Transform, g => g
                .AddMultiple(Strings.Translate, m => m
                    .BindSourceOperator<Configure.Transform.TranslateOperator>()
                    .BindTransform<TranslateTransform>()
                    .BindNode<TranslateTransformNode>()
                )

                .AddMultiple(Strings.Skew, m => m
                    .BindSourceOperator<Configure.Transform.SkewOperator>()
                    .BindTransform<SkewTransform>()
                    .BindNode<SkewTransformNode>()
                )

                .AddMultiple(Strings.Scale, m => m
                    .BindSourceOperator<Configure.Transform.ScaleOperator>()
                    .BindTransform<ScaleTransform>()
                    .BindNode<ScaleTransformNode>()
                )

                .AddMultiple(Strings.Rotation, m => m
                    .BindSourceOperator<Configure.Transform.RotationOperator>()
                    .BindTransform<RotationTransform>()
                    .BindNode<RotationTransformNode>()
                )

                .AddMultiple(Strings.Rotation3D, m => m
                    .BindSourceOperator<Configure.Transform.Rotation3DOperator>()
                    .BindTransform<Rotation3DTransform>()
                    .BindNode<Rotation3DTransformNode>()
                )
            );

        LibraryService.Current
            .RegisterGroup(Strings.FilterEffect, g => g
                .AddMultiple(Strings.Blur, m => m
                    .BindSourceOperator<Configure.Effects.BlurOperator>()
                    .BindFilterEffect<Blur>()
                )

                .AddMultiple(Strings.DropShadow, m => m
                    .BindSourceOperator<Configure.Effects.DropShadowOperator>()
                    .BindFilterEffect<DropShadow>()
                )

                .AddMultiple(Strings.InnerShadow, m => m
                    .BindSourceOperator<Configure.Effects.InnerShadowOperator>()
                    .BindFilterEffect<InnerShadow>()
                )

                .AddMultiple(Strings.FlatShadow, m => m
                    .BindSourceOperator<Configure.Effects.FlatShadowOperator>()
                    .BindFilterEffect<FlatShadow>()
                )

                .AddMultiple($"{Strings.Border} (deprecated)", m => m
                    .BindSourceOperator<Configure.Effects.BorderOperator>()
                    .BindFilterEffect<Border>()
                )

                .AddMultiple(Strings.StrokeEffect, m => m
                    .BindSourceOperator<Configure.Effects.StrokeEffectOperator>()
                    .BindFilterEffect<StrokeEffect>()
                )

                .AddMultiple(Strings.Clipping, m => m
                    .BindSourceOperator<Configure.Effects.ClippingOperator>()
                    .BindFilterEffect<Clipping>()
                )

                .AddMultiple(Strings.Dilate, m => m
                    .BindSourceOperator<Configure.Effects.DilateOperator>()
                    .BindFilterEffect<Dilate>()
                )

                .AddMultiple(Strings.Erode, m => m
                    .BindSourceOperator<Configure.Effects.ErodeOperator>()
                    .BindFilterEffect<Erode>()
                )

                .AddMultiple(Strings.HighContrast, m => m
                    .BindSourceOperator<Configure.Effects.HighContrastOperator>()
                    .BindFilterEffect<HighContrast>()
                )

                .AddMultiple(Strings.HueRotate, m => m
                    .BindSourceOperator<Configure.Effects.HueRotateOperator>()
                    .BindFilterEffect<HueRotate>()
                )

                .AddMultiple(Strings.Lighting, m => m
                    .BindSourceOperator<Configure.Effects.LightingOperator>()
                    .BindFilterEffect<Lighting>()
                )

                .AddMultiple(Strings.LumaColor, m => m
                    .BindSourceOperator<Configure.Effects.LumaColorOperator>()
                    .BindFilterEffect<LumaColor>()
                )

                .AddMultiple(Strings.Saturate, m => m
                    .BindSourceOperator<Configure.Effects.SaturateOperator>()
                    .BindFilterEffect<Saturate>()
                )

                .AddMultiple(Strings.Threshold, m => m
                    .BindSourceOperator<Configure.Effects.ThresholdOperator>()
                    .BindFilterEffect<Threshold>()
                )

                .AddMultiple(Strings.Brightness, m => m
                    .BindSourceOperator<Configure.Effects.BrightnessOperator>()
                    .BindFilterEffect<Brightness>()
                )

                .AddMultiple(Strings.Gamma, m => m
                    .BindSourceOperator<Configure.Effects.GammaOperator>()
                    .BindFilterEffect<Gamma>()
                )

                .AddMultiple(Strings.Invert, m => m
                    .BindSourceOperator<Configure.Effects.InvertOperator>()
                    .BindFilterEffect<Invert>()
                )

                .AddMultiple(Strings.LUT_Cube_File, m => m
                    .BindSourceOperator<Configure.Effects.LutEffectOperator>()
                    .BindFilterEffect<LutEffect>()
                )

                .AddMultiple(Strings.BlendEffect, m => m
                    .BindSourceOperator<Configure.Effects.BlendEffectOperator>()
                    .BindFilterEffect<BlendEffect>()
                )

                .AddMultiple(Strings.Negaposi, m => m
                    .BindSourceOperator<Configure.Effects.NegaposiOperator>()
                    .BindFilterEffect<Negaposi>()
                )

                .AddMultiple(Strings.ChromaKey, m => m
                    .BindSourceOperator<Configure.Effects.ChromaKeyOperator>()
                    .BindFilterEffect<ChromaKey>()
                )

                .AddMultiple(Strings.ColorKey, m => m
                    .BindSourceOperator<Configure.Effects.ColorKeyOperator>()
                    .BindFilterEffect<ColorKey>()
                )

                .AddMultiple(Strings.SplitEquallyEffect, m => m
                    .BindSourceOperator<Configure.Effects.SplitEffectOperator>()
                    .BindFilterEffect<SplitEffect>()
                )

                .AddMultiple(Strings.SplitByPartsEffect, m => m
                    .BindSourceOperator<Configure.Effects.PartsSplitEffectOperator>()
                    .BindFilterEffect<PartsSplitEffect>()
                )

                .AddMultiple(Strings.Transform, m => m
                    .BindSourceOperator<Configure.Effects.TransformEffectOperator>()
                    .BindFilterEffect<TransformEffect>()
                )

                .AddMultiple(Strings.Mosaic, m => m
                    .BindSourceOperator<Configure.Effects.MosaicOperator>()
                    .BindFilterEffect<Mosaic>()
                )

                .AddGroup("OpenCV", gg => gg
                    .AddMultiple("CvBlur", m => m
                        .BindSourceOperator<Configure.Effects.CvBlursOperator>()
                        .BindFilterEffect<Graphics.Effects.OpenCv.Blur>()
                    )
                    .AddMultiple("CvGaussianBlur", m => m
                        .BindSourceOperator<Configure.Effects.CvGaussianBlurOperator>()
                        .BindFilterEffect<Graphics.Effects.OpenCv.GaussianBlur>()
                    )
                    .AddMultiple("CvMedianBlur", m => m
                        .BindSourceOperator<Configure.Effects.CvMedianBlurOperator>()
                        .BindFilterEffect<Graphics.Effects.OpenCv.MedianBlur>()
                    )
                )
            );

        LibraryService.Current
            .RegisterGroup("SoundEffect", g => g
                .AddMultiple("Delay", m => m
                    .BindSourceOperator<Configure.SoundEffect.DelayOperator>()
                    .BindSoundEffect<Delay>()
                )
            );

        LibraryService.Current.RegisterGroup(Strings.Configure, group => group
            .AddSourceOperator<Configure.ConfigureTransformOperator>(Strings.Transform)

            .AddSourceOperator<Configure.AlignmentOperator>(Strings.Alignment)

            .AddSourceOperator<Configure.BlendOperator>(Strings.BlendMode)

            .AddSourceOperator<Configure.FillOperator>(Strings.Fill)

            .AddSourceOperator<Configure.OpacityMaskOperator>(Strings.OpacityMask)
        );
    }
}
