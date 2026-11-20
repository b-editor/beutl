using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Models;
using Beutl.Graphics3D.Primitives;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services;

// The built-in items registered in each host's library.
internal static class LibraryRegistrar
{
    public static void RegisterAll(LibraryService library)
    {
        library
            .AddMultiple(GraphicsStrings.EllipseShape, m => m
                .BindDrawable<Graphics.Shapes.EllipseShape>()
                .BindGeometry<EllipseGeometry>()
            );

        library
            .AddMultiple(GraphicsStrings.RectShape, m => m
                .BindDrawable<Graphics.Shapes.RectShape>()
                .BindGeometry<RectGeometry>()
            );

        library
            .AddMultiple(GraphicsStrings.RoundedRectShape, m => m
                .BindDrawable<Graphics.Shapes.RoundedRectShape>()
                .BindGeometry<RoundedRectGeometry>()
            );

        library
            .AddMultiple(GraphicsStrings.GeometryShape, m => m
                .BindDrawable<Graphics.Shapes.GeometryShape>()
            );

        library
            .AddMultiple(GraphicsStrings.TextBlock, m => m
                .BindDrawable<Graphics.Shapes.TextBlock>()
            );

        library
            .AddMultiple(GraphicsStrings.SourceVideo, m => m
                .BindDrawable<SourceVideo>()
            );

        library
            .AddMultiple(GraphicsStrings.SourceImage, m => m
                .BindDrawable<SourceImage>()
            );

        library
            .AddMultiple(GraphicsStrings.SourceBackdrop, m => m
                .BindDrawable<SourceBackdrop>()
            );

        library
            .AddMultiple(AudioStrings.SourceSound, m => m
                .BindSound<SourceSound>()
            );

        library.RegisterGroup($"3D ({Strings.Experimental})", g => g
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
            .AddMultiple(GraphicsStrings.DrawableObject3D, m => m
                .BindEngineObject<DrawableObject3D>()
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

        library
            .AddMultiple(GraphicsStrings.ParticleEmitter, m => m
                .BindDrawable<ParticleEmitter>()
            );

        library
            .AddMultiple(GraphicsStrings.NodeGraphDrawable, m => m
                .BindDrawable<NodeGraphDrawable>()
            );

        library
            .AddMultiple(Strings.Portal, m => m
                .BindEngineObject<PortalObject>()
            );

        library
            .AddMultiple(GraphicsStrings.SceneDrawable, m => m
                .BindDrawable<SceneDrawable>()
            );

        library
            .AddMultiple(AudioStrings.SceneSound, m => m
                .BindSound<SceneSound>()
            );

        library
            .AddMultiple(GraphicsStrings.DrawableDecorator, m => m
                .BindDrawable<DrawableDecorator>()
            );

        library
            .AddMultiple(GraphicsStrings.Group, m => m
                .BindDrawable<DrawableGroup>()
            );

        library
            .AddMultiple(AudioStrings.SoundGroup, m => m
                .BindSound<SoundGroup>()
            );

        library
            .AddMultiple(GraphicsStrings.DrawableTimeController, m => m
                .BindDrawable<DrawableTimeController>()
            );

        library.RegisterGroup(GraphicsStrings.AudioVisualizer, g => g
            .AddMultiple(GraphicsStrings.AudioWaveform, m => m
                .BindDrawable<AudioWaveformDrawable>()
            )
            .AddMultiple(GraphicsStrings.AudioSpectrum, m => m
                .BindDrawable<AudioSpectrumDrawable>()
            )
            .AddMultiple(GraphicsStrings.AudioSpectrogram, m => m
                .BindDrawable<AudioSpectrogramDrawable>()
            )
        );

        library
            .RegisterGroup(GraphicsStrings.Transform, g => g
                .AddTransform<TranslateTransform>(GraphicsStrings.TranslateTransform)
                .AddTransform<SkewTransform>(GraphicsStrings.SkewTransform)
                .AddTransform<ScaleTransform>(GraphicsStrings.Scale)
                .AddTransform<RotationTransform>(GraphicsStrings.Rotation)
                .AddTransform<Rotation3DTransform>(GraphicsStrings.Rotation3DTransform)
            );

        library
            .RegisterGroup(GraphicsStrings.FilterEffect, g => g
                .AddFilterEffect<Blur>(GraphicsStrings.Blur)
                .AddFilterEffect<Sharpen>(GraphicsStrings.Sharpen)
                .AddFilterEffect<RadialBlur>(GraphicsStrings.RadialBlur)
                .AddFilterEffect<MotionBlur>(GraphicsStrings.MotionBlur)
                .AddFilterEffect<Bokeh>(GraphicsStrings.Bokeh)
                .AddFilterEffect<Bloom>(GraphicsStrings.Bloom)
                .AddFilterEffect<LightRays>(GraphicsStrings.LightRays)
                .AddFilterEffect<WaveRefraction>(GraphicsStrings.WaveRefraction)
                .AddFilterEffect<DropShadow>(GraphicsStrings.DropShadow)
                .AddFilterEffect<InnerShadow>(GraphicsStrings.InnerShadow)
                .AddFilterEffect<FlatShadow>(GraphicsStrings.FlatShadow)
                .AddFilterEffect<StrokeEffect>(GraphicsStrings.Stroke)
                .AddFilterEffect<Clipping>(GraphicsStrings.Clipping)
                .AddFilterEffect<DiagonalClipping>(GraphicsStrings.DiagonalClipping)
                .AddFilterEffect<Dilate>(GraphicsStrings.Dilate)
                .AddFilterEffect<Erode>(GraphicsStrings.Erode)
                .AddFilterEffect<HighContrast>(GraphicsStrings.HighContrast)
                .AddFilterEffect<HueRotate>(GraphicsStrings.HueRotate)
                .AddFilterEffect<Lighting>(GraphicsStrings.Lighting)
                .AddFilterEffect<LumaColor>(GraphicsStrings.LumaColor)
                .AddFilterEffect<Saturate>(GraphicsStrings.Saturate)
                .AddFilterEffect<Threshold>(GraphicsStrings.Threshold)
                .AddFilterEffect<EdgeDetection>(GraphicsStrings.EdgeDetection)
                .AddFilterEffect<Brightness>(GraphicsStrings.Brightness)
                .AddFilterEffect<Gamma>(GraphicsStrings.Gamma)
                .AddFilterEffect<ColorGrading>(GraphicsStrings.ColorGrading)
                .AddFilterEffect<Curves>(GraphicsStrings.Curves)
                .AddFilterEffect<FilmGrain>(GraphicsStrings.FilmGrain)
                .AddFilterEffect<Invert>(GraphicsStrings.Invert)
                .AddFilterEffect<LutEffect>(GraphicsStrings.LutEffect)
                .AddFilterEffect<BlendEffect>(GraphicsStrings.BlendEffect)
                .AddFilterEffect<Negaposi>(GraphicsStrings.Negaposi)
                .AddFilterEffect<ChromaKey>(GraphicsStrings.ChromaKey)
                .AddFilterEffect<ColorKey>(GraphicsStrings.ColorKey)
                .AddFilterEffect<LumaKey>(GraphicsStrings.LumaKey)
                .AddFilterEffect<SplitEffect>(GraphicsStrings.SplitEffect)
                .AddFilterEffect<PartsSplitEffect>(GraphicsStrings.PartsSplitEffect)
                .AddFilterEffect<RepeatEffect>(GraphicsStrings.RepeatEffect)
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
            );

        library
            .RegisterGroup(GraphicsStrings.ClipTransition, g => g
                .AddClipTransition<CrossDissolveTransition>(GraphicsStrings.CrossDissolveTransition)
                .AddClipTransition<FadeTransition>(GraphicsStrings.FadeTransition)
                .AddClipTransition<WipeTransition>(GraphicsStrings.WipeTransition)
                .AddClipTransition<DipToColorTransition>(GraphicsStrings.DipToColorTransition)
                .AddClipTransition<DipToWhiteTransition>(GraphicsStrings.DipToWhiteTransition)
                .AddClipTransition<PushTransition>(GraphicsStrings.PushTransition)
                .AddClipTransition<SlideTransition>(GraphicsStrings.SlideTransition)
                .AddClipTransition<ZoomTransition>(GraphicsStrings.ZoomTransition)
                .AddClipTransition<IrisTransition>(GraphicsStrings.IrisTransition)
                .AddClipTransition<SplitTransition>(GraphicsStrings.SplitTransition)
                .AddClipTransition<BlindsTransition>(GraphicsStrings.BlindsTransition)
                .AddClipTransition<CheckerboardTransition>(GraphicsStrings.CheckerboardTransition)
                .AddClipTransition<ZoomBlurTransition>(GraphicsStrings.ZoomBlurTransition)
                .AddClipTransition<WhipPanTransition>(GraphicsStrings.WhipPanTransition)
                .AddClipTransition<GlitchTransition>(GraphicsStrings.GlitchTransition)
                .AddClipTransition<LightLeakTransition>(GraphicsStrings.LightLeakTransition)
            );

        library
            .RegisterGroup(AudioStrings.AudioEffect, g => g
                .AddAudioEffect<DelayEffect>(AudioStrings.DelayEffect)
                .AddAudioEffect<EqualizerEffect>(AudioStrings.Equalizer)
                .AddAudioEffect<CompressorEffect>(AudioStrings.CompressorEffect)
                .AddAudioEffect<LimiterEffect>(AudioStrings.LimiterEffect)
                .AddAudioEffect<GateEffect>(AudioStrings.GateEffect)
                .AddAudioEffect<TimeStretchEffect>(AudioStrings.TimeStretchEffect)
                .AddAudioEffect<NoiseReductionEffect>(AudioStrings.NoiseReductionEffect)
                .AddAudioEffect<PitchShiftEffect>(AudioStrings.PitchShiftEffect)
                .AddAudioEffect<ResamplingEffect>(AudioStrings.ResamplingEffect)
                .AddAudioEffect<VibratoEffect>(AudioStrings.VibratoEffect)
                .AddAudioEffect<RingModulatorEffect>(AudioStrings.RingModulatorEffect)
            );

        BuiltInLibraryThumbnailRecipes.Register();
    }
}
