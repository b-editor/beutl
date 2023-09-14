using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes.Transform;
using Beutl.Operation;
using Beutl.Services;

namespace Beutl.Operators;

public static class LibraryRegistrar
{
    private static MultipleTypeLibraryItem BindSourceOperator<T>(this MultipleTypeLibraryItem self)
        where T : SourceOperator
    {
        return self.Bind<T>(KnownLibraryItemFormats.SourceOperator);
    }

    private static MultipleTypeLibraryItem BindNode<T>(this MultipleTypeLibraryItem self)
        where T : Node
    {
        return self.Bind<T>(KnownLibraryItemFormats.Node);
    }

    private static MultipleTypeLibraryItem BindEasing<T>(this MultipleTypeLibraryItem self)
        where T : Easing
    {
        return self.Bind<T>(KnownLibraryItemFormats.Easing);
    }

    private static MultipleTypeLibraryItem BindFilterEffect<T>(this MultipleTypeLibraryItem self)
        where T : FilterEffect
    {
        return self.Bind<T>(KnownLibraryItemFormats.FilterEffect);
    }

    private static MultipleTypeLibraryItem BindTransform<T>(this MultipleTypeLibraryItem self)
        where T : Transform
    {
        return self.Bind<T>(KnownLibraryItemFormats.Transform);
    }

    private static MultipleTypeLibraryItem BindDrawable<T>(this MultipleTypeLibraryItem self)
        where T : Drawable
    {
        return self.Bind<T>(KnownLibraryItemFormats.Drawable);
    }

    private static MultipleTypeLibraryItem BindSound<T>(this MultipleTypeLibraryItem self)
        where T : Sound
    {
        return self.Bind<T>(KnownLibraryItemFormats.Sound);
    }

    private static MultipleTypeLibraryItem BindSoundEffect<T>(this MultipleTypeLibraryItem self)
        where T : SoundEffect
    {
        return self.Bind<T>(KnownLibraryItemFormats.SoundEffect);
    }

    private static MultipleTypeLibraryItem BindBrush<T>(this MultipleTypeLibraryItem self)
        where T : Brush
    {
        return self.Bind<T>(KnownLibraryItemFormats.Brush);
    }

    private static MultipleTypeLibraryItem BindGeometry<T>(this MultipleTypeLibraryItem self)
        where T : Geometry
    {
        return self.Bind<T>(KnownLibraryItemFormats.Geometry);
    }

    private static GroupLibraryItem AddSourceOperator<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : SourceOperator
    {
        return self.Add<T>(KnownLibraryItemFormats.SourceOperator, displayName, description);
    }

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

                .AddMultiple(Strings.Border, m => m
                    .BindSourceOperator<Configure.Effects.BorderOperator>()
                    .BindFilterEffect<Border>()
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

                .AddMultiple(Strings.Transform, m => m
                    .BindSourceOperator<Configure.Effects.TransformEffectOperator>()
                    .BindFilterEffect<TransformEffect>()
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
