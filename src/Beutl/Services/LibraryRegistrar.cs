using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Models;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;

namespace Beutl.Services;

public static class LibraryRegistrar
{
    public static void RegisterAll()
    {
        LibraryService.Current
            .AddMultiple(GraphicsStrings.EllipseShape, m => m
                .BindDrawable<Graphics.Shapes.EllipseShape>()
                .BindGeometry<EllipseGeometry>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.RectShape, m => m
                .BindDrawable<Graphics.Shapes.RectShape>()
                .BindGeometry<RectGeometry>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.RoundedRectShape, m => m
                .BindDrawable<Graphics.Shapes.RoundedRectShape>()
                .BindGeometry<RoundedRectGeometry>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.GeometryShape, m => m
                .BindDrawable<Graphics.Shapes.GeometryShape>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.TextBlock, m => m
                .BindDrawable<Graphics.Shapes.TextBlock>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.SourceVideo, m => m
                .BindDrawable<SourceVideo>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.SourceImage, m => m
                .BindDrawable<SourceImage>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.SourceBackdrop, m => m
                .BindDrawable<SourceBackdrop>()
            );

        LibraryService.Current
            .AddMultiple(AudioStrings.SourceSound, m => m
                .BindSound<SourceSound>()
            );

        LibraryService.Current.RegisterGroup($"3D ({Strings.Experimental})", g => g
            .AddMultiple(GraphicsStrings.Scene3D, m => m
                .BindDrawable<Scene3D>()
            )
            .AddMultiple(GraphicsStrings.Cube3D, m => m
                .BindEngineObject<Cube3D>()
            )
            .AddMultiple(GraphicsStrings.Sphere3D, m => m
                .BindEngineObject<Sphere3D>()
            )
            .AddMultiple(GraphicsStrings.Plane3D, m => m
                .BindEngineObject<Plane3D>()
            )
            .AddMultiple(GraphicsStrings.Model3D, m => m
                .BindEngineObject<Model3D>()
            )
            // Lights
            .AddMultiple(GraphicsStrings.DirectionalLight3D, m => m
                .BindEngineObject<DirectionalLight3D>()
            )
            .AddMultiple(GraphicsStrings.PointLight3D, m => m
                .BindEngineObject<PointLight3D>()
            )
            .AddMultiple(GraphicsStrings.SpotLight3D, m => m
                .BindEngineObject<SpotLight3D>()
            )
        );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.ParticleEmitter, m => m
                .BindDrawable<ParticleEmitter>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.NodeGraphDrawable, m => m
                .BindDrawable<NodeGraphDrawable>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Portal, m => m
                .BindEngineObject<PortalObject>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.SceneDrawable, m => m
                .BindDrawable<SceneDrawable>()
            );

        LibraryService.Current
            .AddMultiple(AudioStrings.SceneSound, m => m
                .BindSound<SceneSound>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.DrawableDecorator, m => m
                .BindDrawable<DrawableDecorator>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.Group, m => m
                .BindDrawable<DrawableGroup>()
            );

        LibraryService.Current
            .AddMultiple(AudioStrings.SoundGroup, m => m
                .BindSound<SoundGroup>()
            );

        LibraryService.Current
            .AddMultiple(GraphicsStrings.DrawableTimeController, m => m
                .BindDrawable<DrawableTimeController>()
            );

        LibraryService.Current
            .RegisterGroup(GraphicsStrings.Transform, g => g
                .AddTransform<TranslateTransform>(GraphicsStrings.TranslateTransform)
                .AddTransform<SkewTransform>(GraphicsStrings.SkewTransform)
                .AddTransform<ScaleTransform>(GraphicsStrings.Scale)
                .AddTransform<RotationTransform>(GraphicsStrings.Rotation)
                .AddTransform<Rotation3DTransform>(GraphicsStrings.Rotation3DTransform)
            );

        LibraryService.Current
            .RegisterGroup(GraphicsStrings.FilterEffect, g => g
                .AddFilterEffect<Blur>(GraphicsStrings.Blur)
                .AddFilterEffect<DropShadow>(GraphicsStrings.DropShadow)
                .AddFilterEffect<InnerShadow>(GraphicsStrings.InnerShadow)
                .AddFilterEffect<FlatShadow>(GraphicsStrings.FlatShadow)
                .AddFilterEffect<StrokeEffect>(GraphicsStrings.Stroke)
                .AddFilterEffect<Clipping>(GraphicsStrings.Clipping)
                .AddFilterEffect<Dilate>(GraphicsStrings.Dilate)
                .AddFilterEffect<Erode>(GraphicsStrings.Erode)
                .AddFilterEffect<HighContrast>(GraphicsStrings.HighContrast)
                .AddFilterEffect<HueRotate>(GraphicsStrings.HueRotate)
                .AddFilterEffect<Lighting>(GraphicsStrings.Lighting)
                .AddFilterEffect<LumaColor>(GraphicsStrings.LumaColor)
                .AddFilterEffect<Saturate>(GraphicsStrings.Saturate)
                .AddFilterEffect<Threshold>(GraphicsStrings.Threshold)
                .AddFilterEffect<Brightness>(GraphicsStrings.Brightness)
                .AddFilterEffect<Gamma>(GraphicsStrings.Gamma)
                .AddFilterEffect<ColorGrading>(GraphicsStrings.ColorGrading)
                .AddFilterEffect<Curves>(GraphicsStrings.Curves)
                .AddFilterEffect<Invert>(GraphicsStrings.Invert)
                .AddFilterEffect<LutEffect>(GraphicsStrings.LutEffect)
                .AddFilterEffect<BlendEffect>(GraphicsStrings.BlendEffect)
                .AddFilterEffect<Negaposi>(GraphicsStrings.Negaposi)
                .AddFilterEffect<ChromaKey>(GraphicsStrings.ChromaKey)
                .AddFilterEffect<ColorKey>(GraphicsStrings.ColorKey)
                .AddFilterEffect<SplitEffect>(GraphicsStrings.SplitEffect)
                .AddFilterEffect<PartsSplitEffect>(GraphicsStrings.PartsSplitEffect)
                .AddFilterEffect<TransformEffect>(GraphicsStrings.Transform)
                .AddFilterEffect<MosaicEffect>(GraphicsStrings.MosaicEffect)
                .AddFilterEffect<ColorShift>(GraphicsStrings.ColorShift)
                .AddFilterEffect<ShakeEffect>(GraphicsStrings.ShakeEffect)
                .AddFilterEffect<DisplacementMapEffect>(GraphicsStrings.DisplacementMapEffect)
                .AddFilterEffect<PathFollowEffect>(GraphicsStrings.PathFollowEffect)
                .AddFilterEffect<LayerEffect>(GraphicsStrings.LayerEffect)
                .AddFilterEffect<DelayAnimationEffect>(GraphicsStrings.DelayAnimationEffect)
                .AddFilterEffect<PixelSortEffect>(GraphicsStrings.PixelSortEffect)
                .AddFilterEffect<NodeGraphFilterEffect>(GraphicsStrings.NodeGraphFilterEffect)
                .AddGroup(GraphicsStrings.Script, gg => gg
                    .AddFilterEffect<CSharpScriptEffect>(GraphicsStrings.CSharpScriptEffect)
                    .AddFilterEffect<SKSLScriptEffect>(GraphicsStrings.SKSLScriptEffect)
                    .AddFilterEffect<GLSLScriptEffect>(GraphicsStrings.GLSLScriptEffect)
                )
                .AddGroup(GraphicsStrings.Blur, gg => gg
                    .AddFilterEffect<Graphics.Effects.OpenCv.Blur>("CvBlur")
                    .AddFilterEffect<Graphics.Effects.OpenCv.GaussianBlur>("CvGaussianBlur")
                    .AddFilterEffect<Graphics.Effects.OpenCv.MedianBlur>("CvMedianBlur")
                )
            );

        LibraryService.Current
            .RegisterGroup(AudioStrings.AudioEffect, g => g
                .AddAudioEffect<DelayEffect>(AudioStrings.DelayEffect)
            );
    }
}
