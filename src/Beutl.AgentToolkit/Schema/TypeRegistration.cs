using Beutl.Animation.Easings;
using Beutl.Audio.Effects;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

// The types the schema describes besides those in LibraryService. The toolkit keeps them itself: the editor lists
// every LibraryService item in its Library tab and timeline menu, where these raw type names do not belong.
public static class TypeRegistration
{
    private static readonly object s_lock = new();
    private static readonly List<(string Format, Type Type)> s_types = [];
    private static bool s_registered;

    internal static IReadOnlyList<(string Format, Type Type)> Types
    {
        get
        {
            EnsureRegistered();
            return s_types;
        }
    }

    public static void EnsureRegistered()
    {
        lock (s_lock)
        {
            if (s_registered)
            {
                return;
            }

            // The editor's library items first, so this host describes the same types as the app.
            LibraryRegistrar.RegisterAll();
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

    private static void RegisterTransforms()
    {
        Register<TransformGroup>(KnownLibraryItemFormats.Transform);
        Register<MatrixTransform>(KnownLibraryItemFormats.Transform);
    }

    private static void RegisterFilterEffects()
    {
        Register<FilterEffectGroup>(KnownLibraryItemFormats.FilterEffect);
    }

    private static void RegisterAudioEffects()
    {
        Register<AudioEffectGroup>(KnownLibraryItemFormats.AudioEffect);
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
        Register<GraphModel>(KnownLibraryItemFormats.EngineObject);
    }

    private static void Register<T>(string format) => s_types.Add((format, typeof(T)));
}
