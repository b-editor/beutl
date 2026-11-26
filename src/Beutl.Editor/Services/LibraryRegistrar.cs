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
            .AddMultiple(GraphicsStrings.EllipseShape, GraphicsStrings.EllipseShape_LibraryDescription, m => m
                .BindDrawable<Graphics.Shapes.EllipseShape>()
                .BindGeometry<EllipseGeometry>()
            );

        library
            .AddMultiple(GraphicsStrings.RectShape, GraphicsStrings.RectShape_LibraryDescription, m => m
                .BindDrawable<Graphics.Shapes.RectShape>()
                .BindGeometry<RectGeometry>()
            );

        library
            .AddMultiple(GraphicsStrings.RoundedRectShape, GraphicsStrings.RoundedRectShape_LibraryDescription, m => m
                .BindDrawable<Graphics.Shapes.RoundedRectShape>()
                .BindGeometry<RoundedRectGeometry>()
            );

        library
            .AddMultiple(GraphicsStrings.GeometryShape, GraphicsStrings.GeometryShape_LibraryDescription, m => m
                .BindDrawable<Graphics.Shapes.GeometryShape>()
            );

        library
            .AddMultiple(GraphicsStrings.TextBlock, GraphicsStrings.TextBlock_LibraryDescription, m => m
                .BindDrawable<Graphics.Shapes.TextBlock>()
            );

        library
            .AddMultiple(GraphicsStrings.SourceVideo, GraphicsStrings.SourceVideo_LibraryDescription, m => m
                .BindDrawable<SourceVideo>()
            );

        library
            .AddMultiple(GraphicsStrings.SourceImage, GraphicsStrings.SourceImage_LibraryDescription, m => m
                .BindDrawable<SourceImage>()
            );

        library
            .AddMultiple(GraphicsStrings.SourceBackdrop, GraphicsStrings.SourceBackdrop_LibraryDescription, m => m
                .BindDrawable<SourceBackdrop>()
            );

        library
            .AddMultiple(AudioStrings.SourceSound, AudioStrings.SourceSound_LibraryDescription, m => m
                .BindSound<SourceSound>()
            );

        library.RegisterGroup($"3D ({Strings.Experimental})", null, KnownLibraryGroupIds.ThreeD, g => g
            .AddMultiple(GraphicsStrings.Scene3D, GraphicsStrings.Scene3D_LibraryDescription, m => m
                .BindDrawable<Scene3D>()
            )
            .AddMultiple(GraphicsStrings.Cube3D, GraphicsStrings.Cube3D_LibraryDescription, m => m
                .BindEngineObject<Cube3D>()
            )
            .AddMultiple(GraphicsStrings.Sphere3D, GraphicsStrings.Sphere3D_LibraryDescription, m => m
                .BindEngineObject<Sphere3D>()
            )
            .AddMultiple(GraphicsStrings.Plane3D, GraphicsStrings.Plane3D_LibraryDescription, m => m
                .BindEngineObject<Plane3D>()
            )
            .AddMultiple(GraphicsStrings.Model3D, GraphicsStrings.Model3D_LibraryDescription, m => m
                .BindEngineObject<Model3D>()
            )
            .AddMultiple(GraphicsStrings.DrawableObject3D, GraphicsStrings.DrawableObject3D_LibraryDescription, m => m
                .BindEngineObject<DrawableObject3D>()
            )
            // Lights
            .AddMultiple(GraphicsStrings.DirectionalLight3D,
                GraphicsStrings.DirectionalLight3D_LibraryDescription, m => m
                .BindEngineObject<DirectionalLight3D>()
            )
            .AddMultiple(GraphicsStrings.PointLight3D, GraphicsStrings.PointLight3D_LibraryDescription, m => m
                .BindEngineObject<PointLight3D>()
            )
            .AddMultiple(GraphicsStrings.SpotLight3D, GraphicsStrings.SpotLight3D_LibraryDescription, m => m
                .BindEngineObject<SpotLight3D>()
            )
        );

        library
            .AddMultiple(GraphicsStrings.ParticleEmitter, GraphicsStrings.ParticleEmitter_LibraryDescription, m => m
                .BindDrawable<ParticleEmitter>()
            );

        library
            .AddMultiple(GraphicsStrings.NodeGraphDrawable, GraphicsStrings.NodeGraphDrawable_LibraryDescription, m => m
                .BindDrawable<NodeGraphDrawable>()
            );

        library
            .AddMultiple(Strings.Portal, Strings.Portal_LibraryDescription, m => m
                .BindEngineObject<PortalObject>()
            );

        library
            .AddMultiple(GraphicsStrings.SceneDrawable, GraphicsStrings.SceneDrawable_LibraryDescription, m => m
                .BindDrawable<SceneDrawable>()
            );

        library
            .AddMultiple(AudioStrings.SceneSound, AudioStrings.SceneSound_LibraryDescription, m => m
                .BindSound<SceneSound>()
            );

        library
            .AddMultiple(GraphicsStrings.DrawableDecorator, GraphicsStrings.DrawableDecorator_LibraryDescription, m => m
                .BindDrawable<DrawableDecorator>()
            );

        library
            .AddMultiple(GraphicsStrings.Group, GraphicsStrings.DrawableGroup_LibraryDescription, m => m
                .BindDrawable<DrawableGroup>()
            );

        library
            .AddMultiple(AudioStrings.SoundGroup, AudioStrings.SoundGroup_LibraryDescription, m => m
                .BindSound<SoundGroup>()
            );

        library
            .AddMultiple(GraphicsStrings.DrawableTimeController,
                GraphicsStrings.DrawableTimeController_LibraryDescription, m => m
                .BindDrawable<DrawableTimeController>()
            );

        library.RegisterGroup(GraphicsStrings.AudioVisualizer, null, KnownLibraryGroupIds.AudioVisualizer, g => g
            .AddMultiple(GraphicsStrings.AudioWaveform, GraphicsStrings.AudioWaveformDrawable_LibraryDescription, m => m
                .BindDrawable<AudioWaveformDrawable>()
            )
            .AddMultiple(GraphicsStrings.AudioSpectrum, GraphicsStrings.AudioSpectrumDrawable_LibraryDescription, m => m
                .BindDrawable<AudioSpectrumDrawable>()
            )
            .AddMultiple(GraphicsStrings.AudioSpectrogram,
                GraphicsStrings.AudioSpectrogramDrawable_LibraryDescription, m => m
                .BindDrawable<AudioSpectrogramDrawable>()
            )
        );

        library
            .RegisterGroup(GraphicsStrings.Transform, null, KnownLibraryGroupIds.Transform, g => g
                .AddTransform<TranslateTransform>(GraphicsStrings.TranslateTransform,
                    GraphicsStrings.TranslateTransform_LibraryDescription)
                .AddTransform<SkewTransform>(GraphicsStrings.SkewTransform,
                    GraphicsStrings.SkewTransform_LibraryDescription)
                .AddTransform<ScaleTransform>(GraphicsStrings.Scale, GraphicsStrings.ScaleTransform_LibraryDescription)
                .AddTransform<RotationTransform>(GraphicsStrings.Rotation,
                    GraphicsStrings.RotationTransform_LibraryDescription)
                .AddTransform<Rotation3DTransform>(GraphicsStrings.Rotation3DTransform,
                    GraphicsStrings.Rotation3DTransform_LibraryDescription)
            );

        library
            .RegisterGroup(GraphicsStrings.FilterEffect, null, KnownLibraryGroupIds.FilterEffect, g => g
                .AddGroup(GraphicsStrings.FilterEffectCategory_BlurAndSharpen, null,
                    KnownLibraryGroupIds.FilterEffectBlurAndSharpen, group => group
                    .AddFilterEffect<Blur>(GraphicsStrings.Blur, GraphicsStrings.Blur_LibraryDescription)
                    .AddFilterEffect<Sharpen>(GraphicsStrings.Sharpen, GraphicsStrings.Sharpen_LibraryDescription)
                    .AddFilterEffect<RadialBlur>(GraphicsStrings.RadialBlur,
                        GraphicsStrings.RadialBlur_LibraryDescription)
                    .AddFilterEffect<MotionBlur>(GraphicsStrings.MotionBlur,
                        GraphicsStrings.MotionBlur_LibraryDescription)
                    .AddFilterEffect<Bokeh>(GraphicsStrings.Bokeh, GraphicsStrings.Bokeh_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_ColorCorrection, null,
                    KnownLibraryGroupIds.FilterEffectColorCorrection, group => group
                    .AddFilterEffect<Brightness>(GraphicsStrings.Brightness,
                        GraphicsStrings.Brightness_LibraryDescription)
                    .AddFilterEffect<HighContrast>(GraphicsStrings.HighContrast,
                        GraphicsStrings.HighContrast_LibraryDescription)
                    .AddFilterEffect<Lighting>(GraphicsStrings.Lighting, GraphicsStrings.Lighting_LibraryDescription)
                    .AddFilterEffect<Gamma>(GraphicsStrings.Gamma, GraphicsStrings.Gamma_LibraryDescription)
                    .AddFilterEffect<HueRotate>(GraphicsStrings.HueRotate, GraphicsStrings.HueRotate_LibraryDescription)
                    .AddFilterEffect<Saturate>(GraphicsStrings.Saturate, GraphicsStrings.Saturate_LibraryDescription)
                    .AddFilterEffect<LumaColor>(GraphicsStrings.LumaColor, GraphicsStrings.LumaColor_LibraryDescription)
                    .AddFilterEffect<Threshold>(GraphicsStrings.Threshold, GraphicsStrings.Threshold_LibraryDescription)
                    .AddFilterEffect<ColorGrading>(GraphicsStrings.ColorGrading,
                        GraphicsStrings.ColorGrading_LibraryDescription)
                    .AddFilterEffect<Curves>(GraphicsStrings.Curves, GraphicsStrings.Curves_LibraryDescription)
                    .AddFilterEffect<LutEffect>(GraphicsStrings.LutEffect, GraphicsStrings.LutEffect_LibraryDescription)
                    .AddFilterEffect<Invert>(GraphicsStrings.Invert, GraphicsStrings.Invert_LibraryDescription)
                    .AddFilterEffect<Negaposi>(GraphicsStrings.Negaposi, GraphicsStrings.Negaposi_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_LightAndShadow, null,
                    KnownLibraryGroupIds.FilterEffectLightAndShadow, group => group
                    .AddFilterEffect<Bloom>(GraphicsStrings.Bloom, GraphicsStrings.Bloom_LibraryDescription)
                    .AddFilterEffect<LightRays>(GraphicsStrings.LightRays, GraphicsStrings.LightRays_LibraryDescription)
                    .AddFilterEffect<DropShadow>(GraphicsStrings.DropShadow,
                        GraphicsStrings.DropShadow_LibraryDescription)
                    .AddFilterEffect<InnerShadow>(GraphicsStrings.InnerShadow,
                        GraphicsStrings.InnerShadow_LibraryDescription)
                    .AddFilterEffect<FlatShadow>(GraphicsStrings.FlatShadow,
                        GraphicsStrings.FlatShadow_LibraryDescription)
                    .AddFilterEffect<Vignette>(GraphicsStrings.Vignette, GraphicsStrings.Vignette_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_MaskingAndKeying, null,
                    KnownLibraryGroupIds.FilterEffectMaskingAndKeying, group => group
                    .AddFilterEffect<Clipping>(GraphicsStrings.Clipping, GraphicsStrings.Clipping_LibraryDescription)
                    .AddFilterEffect<DiagonalClipping>(GraphicsStrings.DiagonalClipping,
                        GraphicsStrings.DiagonalClipping_LibraryDescription)
                    .AddFilterEffect<ChromaKey>(GraphicsStrings.ChromaKey, GraphicsStrings.ChromaKey_LibraryDescription)
                    .AddFilterEffect<ColorKey>(GraphicsStrings.ColorKey, GraphicsStrings.ColorKey_LibraryDescription)
                    .AddFilterEffect<LumaKey>(GraphicsStrings.LumaKey, GraphicsStrings.LumaKey_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_TransformAndDistort, null,
                    KnownLibraryGroupIds.FilterEffectTransformAndDistort, group => group
                    .AddFilterEffect<TransformEffect>(GraphicsStrings.Transform,
                        GraphicsStrings.TransformEffect_LibraryDescription)
                    .AddFilterEffect<SplitEffect>(GraphicsStrings.SplitEffect,
                        GraphicsStrings.SplitEffect_LibraryDescription)
                    .AddFilterEffect<PartsSplitEffect>(GraphicsStrings.PartsSplitEffect,
                        GraphicsStrings.PartsSplitEffect_LibraryDescription)
                    .AddFilterEffect<RepeatEffect>(GraphicsStrings.RepeatEffect,
                        GraphicsStrings.RepeatEffect_LibraryDescription)
                    .AddFilterEffect<WaveRefraction>(GraphicsStrings.WaveRefraction,
                        GraphicsStrings.WaveRefraction_LibraryDescription)
                    .AddFilterEffect<PolarCoordinates>(GraphicsStrings.PolarCoordinates,
                        GraphicsStrings.PolarCoordinates_LibraryDescription)
                    .AddFilterEffect<DisplacementMapEffect>(GraphicsStrings.DisplacementMapEffect,
                        GraphicsStrings.DisplacementMapEffect_LibraryDescription)
                    .AddFilterEffect<MeshWarp>(GraphicsStrings.MeshWarp, GraphicsStrings.MeshWarp_LibraryDescription)
                    .AddFilterEffect<CornerPin>(GraphicsStrings.CornerPin, GraphicsStrings.CornerPin_LibraryDescription)
                    .AddFilterEffect<Puppet>(GraphicsStrings.Puppet, GraphicsStrings.Puppet_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_TimeAndAnimation, null,
                    KnownLibraryGroupIds.FilterEffectTimeAndAnimation, group => group
                    .AddFilterEffect<Echo>(GraphicsStrings.Echo, GraphicsStrings.Echo_LibraryDescription)
                    .AddFilterEffect<SteppedMotion>(GraphicsStrings.SteppedMotion,
                        GraphicsStrings.SteppedMotion_LibraryDescription)
                    .AddFilterEffect<ShakeEffect>(GraphicsStrings.ShakeEffect,
                        GraphicsStrings.ShakeEffect_LibraryDescription)
                    .AddFilterEffect<PathFollowEffect>(GraphicsStrings.PathFollowEffect,
                        GraphicsStrings.PathFollowEffect_LibraryDescription)
                    .AddFilterEffect<DelayAnimationEffect>(GraphicsStrings.DelayAnimationEffect,
                        GraphicsStrings.DelayAnimationEffect_LibraryDescription)
                    .AddFilterEffect<RangeAnimatorEffect>(GraphicsStrings.RangeAnimatorEffect,
                        GraphicsStrings.RangeAnimatorEffect_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_Stylize, null,
                    KnownLibraryGroupIds.FilterEffectStylize, group => group
                    .AddFilterEffect<StrokeEffect>(GraphicsStrings.StrokeEffect,
                        GraphicsStrings.StrokeEffect_LibraryDescription)
                    .AddFilterEffect<Dilate>(GraphicsStrings.Dilate, GraphicsStrings.Dilate_LibraryDescription)
                    .AddFilterEffect<Erode>(GraphicsStrings.Erode, GraphicsStrings.Erode_LibraryDescription)
                    .AddFilterEffect<EdgeDetection>(GraphicsStrings.EdgeDetection,
                        GraphicsStrings.EdgeDetection_LibraryDescription)
                    .AddFilterEffect<FilmGrain>(GraphicsStrings.FilmGrain, GraphicsStrings.FilmGrain_LibraryDescription)
                    .AddFilterEffect<MosaicEffect>(GraphicsStrings.MosaicEffect,
                        GraphicsStrings.MosaicEffect_LibraryDescription)
                    .AddFilterEffect<ColorShift>(GraphicsStrings.ColorShift,
                        GraphicsStrings.ColorShift_LibraryDescription)
                    .AddFilterEffect<LensChromaticAberration>(GraphicsStrings.LensChromaticAberration,
                        GraphicsStrings.LensChromaticAberration_LibraryDescription)
                    .AddFilterEffect<PixelSortEffect>(GraphicsStrings.PixelSortEffect,
                        GraphicsStrings.PixelSortEffect_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_Compositing, null,
                    KnownLibraryGroupIds.FilterEffectCompositing, group => group
                    .AddFilterEffect<OpacityEffect>(GraphicsStrings.OpacityEffect,
                        GraphicsStrings.OpacityEffect_LibraryDescription)
                    .AddFilterEffect<BlendEffect>(GraphicsStrings.BlendEffect,
                        GraphicsStrings.BlendEffect_LibraryDescription)
                    .AddFilterEffect<LayerEffect>(GraphicsStrings.LayerEffect,
                        GraphicsStrings.LayerEffect_LibraryDescription)
                )
                .AddGroup(GraphicsStrings.FilterEffectCategory_ScriptAndNodes, null,
                    KnownLibraryGroupIds.FilterEffectScriptAndNodes, group => group
                    .AddFilterEffect<NodeGraphFilterEffect>(GraphicsStrings.NodeGraphFilterEffect,
                        GraphicsStrings.NodeGraphFilterEffect_LibraryDescription)
                    .AddFilterEffect<CSharpScriptEffect>(GraphicsStrings.CSharpScriptEffect,
                        GraphicsStrings.CSharpScriptEffect_LibraryDescription)
                    .AddFilterEffect<SKSLScriptEffect>(GraphicsStrings.SKSLScriptEffect,
                        GraphicsStrings.SKSLScriptEffect_LibraryDescription)
                    .AddFilterEffect<GLSLScriptEffect>(GraphicsStrings.GLSLScriptEffect,
                        GraphicsStrings.GLSLScriptEffect_LibraryDescription)
                )
            );

        library
            .RegisterGroup(GraphicsStrings.ClipTransition, null, KnownLibraryGroupIds.ClipTransition, g => g
                .AddClipTransition<CrossDissolveTransition>(GraphicsStrings.CrossDissolveTransition,
                    GraphicsStrings.CrossDissolveTransition_LibraryDescription)
                .AddClipTransition<FadeTransition>(GraphicsStrings.FadeTransition,
                    GraphicsStrings.FadeTransition_LibraryDescription)
                .AddClipTransition<WipeTransition>(GraphicsStrings.WipeTransition,
                    GraphicsStrings.WipeTransition_LibraryDescription)
                .AddClipTransition<DipToColorTransition>(GraphicsStrings.DipToColorTransition,
                    GraphicsStrings.DipToColorTransition_LibraryDescription)
                .AddClipTransition<DipToWhiteTransition>(GraphicsStrings.DipToWhiteTransition,
                    GraphicsStrings.DipToWhiteTransition_LibraryDescription)
                .AddClipTransition<PushTransition>(GraphicsStrings.PushTransition,
                    GraphicsStrings.PushTransition_LibraryDescription)
                .AddClipTransition<SlideTransition>(GraphicsStrings.SlideTransition,
                    GraphicsStrings.SlideTransition_LibraryDescription)
                .AddClipTransition<ZoomTransition>(GraphicsStrings.ZoomTransition,
                    GraphicsStrings.ZoomTransition_LibraryDescription)
                .AddClipTransition<IrisTransition>(GraphicsStrings.IrisTransition,
                    GraphicsStrings.IrisTransition_LibraryDescription)
                .AddClipTransition<SplitTransition>(GraphicsStrings.SplitTransition,
                    GraphicsStrings.SplitTransition_LibraryDescription)
                .AddClipTransition<BlindsTransition>(GraphicsStrings.BlindsTransition,
                    GraphicsStrings.BlindsTransition_LibraryDescription)
                .AddClipTransition<CheckerboardTransition>(GraphicsStrings.CheckerboardTransition,
                    GraphicsStrings.CheckerboardTransition_LibraryDescription)
                .AddClipTransition<ZoomBlurTransition>(GraphicsStrings.ZoomBlurTransition,
                    GraphicsStrings.ZoomBlurTransition_LibraryDescription)
                .AddClipTransition<WhipPanTransition>(GraphicsStrings.WhipPanTransition,
                    GraphicsStrings.WhipPanTransition_LibraryDescription)
                .AddClipTransition<GlitchTransition>(GraphicsStrings.GlitchTransition,
                    GraphicsStrings.GlitchTransition_LibraryDescription)
                .AddClipTransition<LightLeakTransition>(GraphicsStrings.LightLeakTransition,
                    GraphicsStrings.LightLeakTransition_LibraryDescription)
            );

        library
            .RegisterGroup(AudioStrings.AudioEffect, null, KnownLibraryGroupIds.AudioEffect, g => g
                .AddGroup(AudioStrings.AudioEffectCategory_EqAndCleanup, null,
                    KnownLibraryGroupIds.AudioEffectEqAndCleanup, group => group
                    .AddAudioEffect<EqualizerEffect>(AudioStrings.Equalizer,
                        AudioStrings.EqualizerEffect_LibraryDescription)
                    .AddAudioEffect<NoiseReductionEffect>(AudioStrings.NoiseReductionEffect,
                        AudioStrings.NoiseReductionEffect_LibraryDescription)
                )
                .AddGroup(AudioStrings.AudioEffectCategory_Dynamics, null,
                    KnownLibraryGroupIds.AudioEffectDynamics, group => group
                    .AddAudioEffect<CompressorEffect>(AudioStrings.CompressorEffect,
                        AudioStrings.CompressorEffect_LibraryDescription)
                    .AddAudioEffect<LimiterEffect>(AudioStrings.LimiterEffect,
                        AudioStrings.LimiterEffect_LibraryDescription)
                    .AddAudioEffect<GateEffect>(AudioStrings.GateEffect, AudioStrings.GateEffect_LibraryDescription)
                )
                .AddGroup(AudioStrings.AudioEffectCategory_TimeAndPitch, null,
                    KnownLibraryGroupIds.AudioEffectTimeAndPitch, group => group
                    .AddAudioEffect<TimeStretchEffect>(AudioStrings.TimeStretchEffect,
                        AudioStrings.TimeStretchEffect_LibraryDescription)
                    .AddAudioEffect<PitchShiftEffect>(AudioStrings.PitchShiftEffect,
                        AudioStrings.PitchShiftEffect_LibraryDescription)
                    .AddAudioEffect<ResamplingEffect>(AudioStrings.ResamplingEffect,
                        AudioStrings.ResamplingEffect_LibraryDescription)
                )
                .AddGroup(AudioStrings.AudioEffectCategory_DelayAndModulation, null,
                    KnownLibraryGroupIds.AudioEffectDelayAndModulation, group => group
                    .AddAudioEffect<DelayEffect>(AudioStrings.DelayEffect, AudioStrings.DelayEffect_LibraryDescription)
                    .AddAudioEffect<VibratoEffect>(AudioStrings.VibratoEffect,
                        AudioStrings.VibratoEffect_LibraryDescription)
                    .AddAudioEffect<RingModulatorEffect>(AudioStrings.RingModulatorEffect,
                        AudioStrings.RingModulatorEffect_LibraryDescription)
                )
            );

        BuiltInLibraryThumbnailRecipes.Register();
    }
}
