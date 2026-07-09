using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Models;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

public static class TypeRegistration
{
    private static readonly object s_lock = new();
    private static bool s_registered;

    public static void EnsureRegistered()
    {
        lock (s_lock)
        {
            if (s_registered)
            {
                return;
            }

            RegisterDrawables();
            RegisterSounds();
            RegisterTransforms();
            RegisterFilterEffects();
            RegisterAudioEffects();
            RegisterBrushes();
            RegisterPens();
            RegisterGeometries();
            RegisterEasings();
            RegisterEngineObjects();

            s_registered = true;
        }
    }

    private static void RegisterDrawables()
    {
        Register<SourceImage>(KnownLibraryItemFormats.Drawable);
        Register<SourceVideo>(KnownLibraryItemFormats.Drawable);
        Register<SourceBackdrop>(KnownLibraryItemFormats.Drawable);
        Register<TextBlock>(KnownLibraryItemFormats.Drawable);
        Register<RectShape>(KnownLibraryItemFormats.Drawable);
        Register<RoundedRectShape>(KnownLibraryItemFormats.Drawable);
        Register<EllipseShape>(KnownLibraryItemFormats.Drawable);
        Register<GeometryShape>(KnownLibraryItemFormats.Drawable);
        Register<DrawableGroup>(KnownLibraryItemFormats.Drawable);
        Register<DrawableDecorator>(KnownLibraryItemFormats.Drawable);
        Register<DrawableTimeController>(KnownLibraryItemFormats.Drawable);
        Register<ParticleEmitter>(KnownLibraryItemFormats.Drawable);
        Register<AudioWaveformDrawable>(KnownLibraryItemFormats.Drawable);
        Register<AudioSpectrumDrawable>(KnownLibraryItemFormats.Drawable);
        Register<AudioSpectrogramDrawable>(KnownLibraryItemFormats.Drawable);
        Register<SceneDrawable>(KnownLibraryItemFormats.Drawable);
        Register<Scene3D>(KnownLibraryItemFormats.Drawable);
        Register<NodeGraphDrawable>(KnownLibraryItemFormats.Drawable);
    }

    private static void RegisterSounds()
    {
        Register<SourceSound>(KnownLibraryItemFormats.Sound);
        Register<SoundGroup>(KnownLibraryItemFormats.Sound);
        Register<SceneSound>(KnownLibraryItemFormats.Sound);
    }

    private static void RegisterTransforms()
    {
        Register<TransformGroup>(KnownLibraryItemFormats.Transform);
        Register<TranslateTransform>(KnownLibraryItemFormats.Transform);
        Register<SkewTransform>(KnownLibraryItemFormats.Transform);
        Register<ScaleTransform>(KnownLibraryItemFormats.Transform);
        Register<RotationTransform>(KnownLibraryItemFormats.Transform);
        Register<Rotation3DTransform>(KnownLibraryItemFormats.Transform);
        Register<MatrixTransform>(KnownLibraryItemFormats.Transform);
    }

    private static void RegisterFilterEffects()
    {
        Register<FilterEffectGroup>(KnownLibraryItemFormats.FilterEffect);
        Register<Blur>(KnownLibraryItemFormats.FilterEffect);
        Register<DropShadow>(KnownLibraryItemFormats.FilterEffect);
        Register<InnerShadow>(KnownLibraryItemFormats.FilterEffect);
        Register<FlatShadow>(KnownLibraryItemFormats.FilterEffect);
        Register<StrokeEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<Clipping>(KnownLibraryItemFormats.FilterEffect);
        Register<Dilate>(KnownLibraryItemFormats.FilterEffect);
        Register<Erode>(KnownLibraryItemFormats.FilterEffect);
        Register<HighContrast>(KnownLibraryItemFormats.FilterEffect);
        Register<HueRotate>(KnownLibraryItemFormats.FilterEffect);
        Register<Lighting>(KnownLibraryItemFormats.FilterEffect);
        Register<LumaColor>(KnownLibraryItemFormats.FilterEffect);
        Register<Saturate>(KnownLibraryItemFormats.FilterEffect);
        Register<Threshold>(KnownLibraryItemFormats.FilterEffect);
        Register<Brightness>(KnownLibraryItemFormats.FilterEffect);
        Register<Gamma>(KnownLibraryItemFormats.FilterEffect);
        Register<ColorGrading>(KnownLibraryItemFormats.FilterEffect);
        Register<Curves>(KnownLibraryItemFormats.FilterEffect);
        Register<Invert>(KnownLibraryItemFormats.FilterEffect);
        Register<LutEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<BlendEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<Negaposi>(KnownLibraryItemFormats.FilterEffect);
        Register<ChromaKey>(KnownLibraryItemFormats.FilterEffect);
        Register<ColorKey>(KnownLibraryItemFormats.FilterEffect);
        Register<SplitEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<PartsSplitEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<TransformEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<MosaicEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<ColorShift>(KnownLibraryItemFormats.FilterEffect);
        Register<ShakeEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<DisplacementMapEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<PathFollowEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<LayerEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<DelayAnimationEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<PixelSortEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<CSharpScriptEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<SKSLScriptEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<GLSLScriptEffect>(KnownLibraryItemFormats.FilterEffect);
        Register<NodeGraphFilterEffect>(KnownLibraryItemFormats.FilterEffect);
    }

    private static void RegisterAudioEffects()
    {
        Register<AudioEffectGroup>(KnownLibraryItemFormats.AudioEffect);
        Register<DelayEffect>(KnownLibraryItemFormats.AudioEffect);
        Register<EqualizerEffect>(KnownLibraryItemFormats.AudioEffect);
        Register<CompressorEffect>(KnownLibraryItemFormats.AudioEffect);
        Register<LimiterEffect>(KnownLibraryItemFormats.AudioEffect);
    }

    private static void RegisterBrushes()
    {
        Register<SolidColorBrush>(KnownLibraryItemFormats.Brush);
        Register<LinearGradientBrush>(KnownLibraryItemFormats.Brush);
        Register<RadialGradientBrush>(KnownLibraryItemFormats.Brush);
        Register<ConicGradientBrush>(KnownLibraryItemFormats.Brush);
        Register<PerlinNoiseBrush>(KnownLibraryItemFormats.Brush);
        Register<ImageBrush>(KnownLibraryItemFormats.Brush);
        Register<DrawableBrush>(KnownLibraryItemFormats.Brush);
    }

    private static void RegisterPens()
    {
        Register<Pen>(KnownLibraryItemFormats.Pen);
    }

    private static void RegisterGeometries()
    {
        Register<RectGeometry>(KnownLibraryItemFormats.Geometry);
        Register<RoundedRectGeometry>(KnownLibraryItemFormats.Geometry);
        Register<EllipseGeometry>(KnownLibraryItemFormats.Geometry);
        Register<PathGeometry>(KnownLibraryItemFormats.Geometry);
    }

    private static void RegisterEasings()
    {
        Register<LinearEasing>(KnownLibraryItemFormats.Easing);
        Register<HoldEasing>(KnownLibraryItemFormats.Easing);
        Register<SineEaseIn>(KnownLibraryItemFormats.Easing);
        Register<SineEaseOut>(KnownLibraryItemFormats.Easing);
        Register<SineEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<QuadraticEaseIn>(KnownLibraryItemFormats.Easing);
        Register<QuadraticEaseOut>(KnownLibraryItemFormats.Easing);
        Register<QuadraticEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<CubicEaseIn>(KnownLibraryItemFormats.Easing);
        Register<CubicEaseOut>(KnownLibraryItemFormats.Easing);
        Register<CubicEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<QuarticEaseIn>(KnownLibraryItemFormats.Easing);
        Register<QuarticEaseOut>(KnownLibraryItemFormats.Easing);
        Register<QuarticEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<QuinticEaseIn>(KnownLibraryItemFormats.Easing);
        Register<QuinticEaseOut>(KnownLibraryItemFormats.Easing);
        Register<QuinticEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<CircularEaseIn>(KnownLibraryItemFormats.Easing);
        Register<CircularEaseOut>(KnownLibraryItemFormats.Easing);
        Register<CircularEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<ExponentialEaseIn>(KnownLibraryItemFormats.Easing);
        Register<ExponentialEaseOut>(KnownLibraryItemFormats.Easing);
        Register<ExponentialEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<BackEaseIn>(KnownLibraryItemFormats.Easing);
        Register<BackEaseOut>(KnownLibraryItemFormats.Easing);
        Register<BackEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<BounceEaseIn>(KnownLibraryItemFormats.Easing);
        Register<BounceEaseOut>(KnownLibraryItemFormats.Easing);
        Register<BounceEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<ElasticEaseIn>(KnownLibraryItemFormats.Easing);
        Register<ElasticEaseOut>(KnownLibraryItemFormats.Easing);
        Register<ElasticEaseInOut>(KnownLibraryItemFormats.Easing);
        Register<SplineEasing>(KnownLibraryItemFormats.Easing);
    }

    private static void RegisterEngineObjects()
    {
        Register<GradientStop>(KnownLibraryItemFormats.EngineObject);
        Register<EqualizerBand>(KnownLibraryItemFormats.EngineObject);
        Register<PathFigure>(KnownLibraryItemFormats.EngineObject);
        Register<LineSegment>(KnownLibraryItemFormats.EngineObject);
        Register<ArcSegment>(KnownLibraryItemFormats.EngineObject);
        Register<ConicSegment>(KnownLibraryItemFormats.EngineObject);
        Register<CubicBezierSegment>(KnownLibraryItemFormats.EngineObject);
        Register<QuadraticBezierSegment>(KnownLibraryItemFormats.EngineObject);
        Register<DisplacementMapTranslateTransform>(KnownLibraryItemFormats.EngineObject);
        Register<DisplacementMapScaleTransform>(KnownLibraryItemFormats.EngineObject);
        Register<DisplacementMapRotationTransform>(KnownLibraryItemFormats.EngineObject);
        Register<Cube3D>(KnownLibraryItemFormats.EngineObject);
        Register<Sphere3D>(KnownLibraryItemFormats.EngineObject);
        Register<Plane3D>(KnownLibraryItemFormats.EngineObject);
        Register<Model3D>(KnownLibraryItemFormats.EngineObject);
        Register<DirectionalLight3D>(KnownLibraryItemFormats.EngineObject);
        Register<PointLight3D>(KnownLibraryItemFormats.EngineObject);
        Register<SpotLight3D>(KnownLibraryItemFormats.EngineObject);
        Register<PortalObject>(KnownLibraryItemFormats.EngineObject);
        Register<GraphModel>(KnownLibraryItemFormats.EngineObject);
    }

    private static void Register<T>(string format)
    {
        if (!LibraryService.Current.GetTypesFromFormat(format).Contains(typeof(T)))
        {
            LibraryService.Current.Register<T>(format, typeof(T).Name);
        }
    }
}
