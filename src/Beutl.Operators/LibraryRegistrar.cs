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
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeTree;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Operators;

public static class LibraryRegistrar
{
    public static void RegisterAll()
    {
        LibraryService.Current
            .AddMultiple(Strings.Ellipse, m => m
                .BindDrawable<Graphics.Shapes.EllipseShape>()
                .BindGeometry<EllipseGeometry>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Rectangle, m => m
                .BindDrawable<Graphics.Shapes.RectShape>()
                .BindGeometry<RectGeometry>()
            );

        LibraryService.Current
            .AddMultiple(Strings.RoundedRect, m => m
                .BindDrawable<Graphics.Shapes.RoundedRectShape>()
                .BindGeometry<RoundedRectGeometry>()
            );

        LibraryService.Current
            .AddMultiple(Strings.GeometryShape, m => m
                .BindDrawable<Graphics.Shapes.GeometryShape>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Text, m => m
                .BindDrawable<Graphics.Shapes.TextBlock>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Video, m => m
                .BindDrawable<SourceVideo>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Image, m => m
                .BindDrawable<SourceImage>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Backdrop, m => m
                .BindDrawable<SourceBackdrop>()
            );

        LibraryService.Current
            .AddMultiple(AudioStrings.SourceSound, m => m
                .BindSound<SourceSound>()
            );

        LibraryService.Current.RegisterGroup($"3D ({Strings.Experimental})", g => g
            .AddMultiple(Strings.Scene3D, m => m
                .BindDrawable<Scene3D>()
            )
            .AddMultiple(Strings.Cube3D, m => m
                .BindEngineObject<Cube3D>()
            )
            .AddMultiple(Strings.Sphere3D, m => m
                .BindEngineObject<Sphere3D>()
            )
            .AddMultiple(Strings.Plane3D, m => m
                .BindEngineObject<Plane3D>()
            )
            .AddMultiple(Strings.Model3D, m => m
                .BindEngineObject<Model3D>()
            )
            // Lights
            .AddMultiple(Strings.DirectionalLight3D, m => m
                .BindEngineObject<DirectionalLight3D>()
            )
            .AddMultiple(Strings.PointLight3D, m => m
                .BindEngineObject<PointLight3D>()
            )
            .AddMultiple(Strings.SpotLight3D, m => m
                .BindEngineObject<SpotLight3D>()
            )
        );

        LibraryService.Current
            .AddMultiple(Strings.ParticleEmitter, m => m
                .BindDrawable<ParticleEmitter>()
            );

        LibraryService.Current
            .AddMultiple(Strings.NodeTree, m => m
                .BindDrawable<NodeTreeDrawable>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Portal, m => m
                .BindEngineObject<TakeAfterPortal>()
            );

        LibraryService.Current
            .AddMultiple(Strings.SceneGraphicsReference, m => m
                .BindDrawable<SceneDrawable>()
            );

        LibraryService.Current
            .AddMultiple(Strings.SceneSoundReference, m => m
                .BindSound<SceneSound>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Decorator, m => m
                .BindDrawable<DrawableDecorator>()
            );

        LibraryService.Current
            .AddMultiple(Strings.Group, m => m
                .BindDrawable<DrawableGroup>()
            );

        LibraryService.Current
            .AddMultiple(AudioStrings.SoundGroup, m => m
                .BindSound<SoundGroup>()
            );

        LibraryService.Current
            .AddMultiple(Strings.TimeController, m => m
                .BindDrawable<DrawableTimeController>()
            );

        LibraryService.Current
            .RegisterGroup(Strings.Transform, g => g
                .AddTransform<TranslateTransform>(Strings.Translate)
                .AddTransform<SkewTransform>(Strings.Skew)
                .AddTransform<ScaleTransform>(Strings.Scale)
                .AddTransform<RotationTransform>(Strings.Rotation)
                .AddTransform<Rotation3DTransform>(Strings.Rotation3D)
            );

        LibraryService.Current
            .RegisterGroup(Strings.FilterEffect, g => g
                .AddFilterEffect<Blur>(Strings.Blur)
                .AddFilterEffect<DropShadow>(Strings.DropShadow)
                .AddFilterEffect<InnerShadow>(Strings.InnerShadow)
                .AddFilterEffect<FlatShadow>(Strings.FlatShadow)
                .AddFilterEffect<StrokeEffect>(Strings.StrokeEffect)
                .AddFilterEffect<Clipping>(Strings.Clipping)
                .AddFilterEffect<Dilate>(Strings.Dilate)
                .AddFilterEffect<Erode>(Strings.Erode)
                .AddFilterEffect<HighContrast>(Strings.HighContrast)
                .AddFilterEffect<HueRotate>(Strings.HueRotate)
                .AddFilterEffect<Lighting>(Strings.Lighting)
                .AddFilterEffect<LumaColor>(Strings.LumaColor)
                .AddFilterEffect<Saturate>(Strings.Saturate)
                .AddFilterEffect<Threshold>(Strings.Threshold)
                .AddFilterEffect<Brightness>(Strings.Brightness)
                .AddFilterEffect<Gamma>(Strings.Gamma)
                .AddFilterEffect<ColorGrading>(Strings.ColorGrading)
                .AddFilterEffect<Curves>(Strings.Curves)
                .AddFilterEffect<Invert>(Strings.Invert)
                .AddFilterEffect<LutEffect>(Strings.LUT_Cube_File)
                .AddFilterEffect<BlendEffect>(Strings.BlendEffect)
                .AddFilterEffect<Negaposi>(Strings.Negaposi)
                .AddFilterEffect<ChromaKey>(Strings.ChromaKey)
                .AddFilterEffect<ColorKey>(Strings.ColorKey)
                .AddFilterEffect<SplitEffect>(Strings.SplitEquallyEffect)
                .AddFilterEffect<PartsSplitEffect>(Strings.SplitByPartsEffect)
                .AddFilterEffect<TransformEffect>(Strings.Transform)
                .AddFilterEffect<MosaicEffect>(Strings.Mosaic)
                .AddFilterEffect<ColorShift>(Strings.ColorShift)
                .AddFilterEffect<ShakeEffect>(Strings.ShakeEffect)
                .AddFilterEffect<DisplacementMapEffect>(Strings.DisplacementMap)
                .AddFilterEffect<PathFollowEffect>(Strings.PathFollowEffect)
                .AddFilterEffect<LayerEffect>(Strings.Layer)
                .AddFilterEffect<DelayAnimationEffect>(Strings.DelayAnimationEffect)
                .AddFilterEffect<PixelSortEffect>(Strings.PixelSort)
                .AddGroup(Strings.Script, gg => gg
                    .AddFilterEffect<CSharpScriptEffect>(Strings.CSharpScriptEffect)
                    .AddFilterEffect<SKSLScriptEffect>(Strings.SKSLScriptEffect)
                    .AddFilterEffect<GLSLScriptEffect>(Strings.GLSLScriptEffect)
                )
                .AddGroup("OpenCV", gg => gg
                    .AddFilterEffect<Graphics.Effects.OpenCv.Blur>("CvBlur")
                    .AddFilterEffect<Graphics.Effects.OpenCv.GaussianBlur>("CvGaussianBlur")
                    .AddFilterEffect<Graphics.Effects.OpenCv.MedianBlur>("CvMedianBlur")
                )
            );

        LibraryService.Current
            .RegisterGroup(Strings.AudioEffect, g => g
                .AddAudioEffect<DelayEffect>(AudioStrings.DelayEffect)
            );
    }
}
