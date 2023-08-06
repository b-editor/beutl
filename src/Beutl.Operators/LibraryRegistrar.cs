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
        LibraryService.Current.RegisterGroup(Strings.Source, group => group
            .AddMultiple(Strings.Ellipse, m => m
                .BindSourceOperator<Source.EllipseOperator>()
                .BindDrawable<Graphics.Shapes.EllipseShape>()
                .BindNode<NodeTree.Nodes.Geometry.EllipseGeometryNode>()
                .BindGeometry<EllipseGeometry>()
            )

            .AddMultiple(Strings.Rectangle, m => m
                .BindSourceOperator<Source.RectOperator>()
                .BindDrawable<Graphics.Shapes.RectShape>()
                .BindNode<NodeTree.Nodes.Geometry.RectGeometryNode>()
                .BindGeometry<RectGeometry>()
            )

            .AddMultiple(Strings.RoundedRect, m => m
                .BindSourceOperator<Source.RoundedRectOperator>()
                .BindDrawable<Graphics.Shapes.RoundedRectShape>()
                .BindNode<NodeTree.Nodes.Geometry.RoundedRectGeometryNode>()
                .BindGeometry<RoundedRectGeometry>()
            )

            .AddMultiple(Strings.Text, m => m
                .BindSourceOperator<Source.TextBlockOperator>()
                .BindDrawable<Graphics.Shapes.TextBlock>()
            )

            .AddMultiple("SourceVideo", m => m
                .BindSourceOperator<Source.SourceVideoOperator>()
                .BindDrawable<SourceVideo>()
            )

            .AddMultiple("SourceImage", m => m
                .BindSourceOperator<Source.SourceImageOperator>()
                .BindDrawable<SourceImage>()
            )

            .AddMultiple("SourceSound", m => m
                .BindSourceOperator<Source.SourceSoundOperator>()
                .BindSound<SourceSound>()
            )
        );

        LibraryService.Current.RegisterGroup(Strings.Configure, group => group
            .AddGroup(Strings.Transform, g => g
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
            )

            .AddGroup(Strings.ImageFilter, g => g
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

                .AddGroup("OpenCV", gg => gg
                    .AddMultiple(Strings.Blur, m => m
                        .BindSourceOperator<Configure.Effects.CvBlurOperator>()
                        .BindFilterEffect<Graphics.Effects.OpenCv.Blur>()
                    )
                )
            )

            .AddGroup("SoundEffect", g => g
                .AddMultiple("Delay", m => m
                    .BindSourceOperator<Configure.SoundEffect.DelayOperator>()
                    .BindSoundEffect<Delay>()
                )
            )

            .AddGroup("SoundEffect", g => g
                .AddMultiple("Delay", m => m
                    .BindSourceOperator<Configure.SoundEffect.DelayOperator>()
                    .BindSoundEffect<Delay>()
                )
            )

            .AddSourceOperator<Configure.AlignmentOperator>(Strings.Alignment)

            .AddSourceOperator<Configure.BlendOperator>(Strings.BlendMode)

            .AddSourceOperator<Configure.ForegroundOperator>(Strings.Foreground)

            .AddSourceOperator<Configure.OpacityMaskOperator>(Strings.OpacityMask)
        );
    }
}
