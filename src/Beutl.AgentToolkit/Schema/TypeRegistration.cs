using Beutl.Animation.Easings;
using Beutl.Audio.Effects;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Editor.Services;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Materials;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

// The types the schema describes besides those in LibraryService. The toolkit keeps them itself: the editor lists
// every LibraryService item in its Library tab and timeline menu, where these raw type names do not belong.
public static class TypeRegistration
{
    private static readonly object s_lock = new();
    private static readonly List<(string Format, Type Type)> s_types = [];
    private static readonly Dictionary<Type, string> s_descriptions = [];
    private static bool s_registered;

    internal static IReadOnlyList<(string Format, Type Type)> Types
    {
        get
        {
            EnsureRegistered();
            return s_types;
        }
    }

    internal static string? FindDescription(Type type) => s_descriptions.GetValueOrDefault(type);

    public static LibraryService CreateLibraryService()
    {
        var library = new LibraryService();
        LibraryRegistrar.RegisterAll(library);
        EnsureRegistered();
        return library;
    }

    public static void EnsureRegistered()
    {
        lock (s_lock)
        {
            if (s_registered)
            {
                return;
            }

            RegisterTransforms();
            RegisterFilterEffects();
            RegisterAudioEffects();
            RegisterBrushes();
            RegisterPens();
            RegisterGeometries();
            RegisterEasings();
            RegisterEngineObjects();
            RegisterElement();
            DescribeNodeGraphs();

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
        Register<Group3D>(KnownLibraryItemFormats.EngineObject);
        Register<PerspectiveCamera>(KnownLibraryItemFormats.EngineObject);
        Register<OrthographicCamera>(KnownLibraryItemFormats.EngineObject);
        Register<BasicMaterial>(KnownLibraryItemFormats.EngineObject);
        Register<PBRMaterial>(KnownLibraryItemFormats.EngineObject);
        Register<UnlitMaterial>(KnownLibraryItemFormats.EngineObject);
        Register<TransparentMaterial>(KnownLibraryItemFormats.EngineObject);
        Register<BarSpectrumShape>(KnownLibraryItemFormats.EngineObject);
        Register<FilledAreaSpectrumShape>(KnownLibraryItemFormats.EngineObject);
        Register<LineSpectrumShape>(KnownLibraryItemFormats.EngineObject);
        Register<MirroredBarsSpectrumShape>(KnownLibraryItemFormats.EngineObject);
        Register<RadialSpectrumShape>(KnownLibraryItemFormats.EngineObject);
        Register<BlockWaveformShape>(KnownLibraryItemFormats.EngineObject);
        Register<DotsWaveformShape>(KnownLibraryItemFormats.EngineObject);
        Register<FilledEnvelopeWaveformShape>(KnownLibraryItemFormats.EngineObject);
        Register<FilledMirrorWaveformShape>(KnownLibraryItemFormats.EngineObject);
        Register<LineWaveformShape>(KnownLibraryItemFormats.EngineObject);
        Register<MinMaxBarWaveformShape>(KnownLibraryItemFormats.EngineObject);
        Register<RadialWaveformShape>(KnownLibraryItemFormats.EngineObject);
    }

    // Not an engine object, but documents write its timing and track matte fields directly.
    private static void RegisterElement()
    {
        Register<Element>(typeof(Element).FullName!);
        s_descriptions[typeof(Element)] =
            "A timeline clip. Use '$type': '[Beutl.ProjectSystem]:Element' only for new entries in Elements; put concrete drawable/effect/brush/transform objects under Objects using discriminators returned by get_schema. EnterTransition and ExitTransition take a ClipTransition from get_schema(category=transition). For structure, call get_examples(name: 'insert-new-element-skeleton').";
    }

    // Their Model is a node graph, whose nodes connect by port Ids that a document cannot know before the ports exist.
    private static void DescribeNodeGraphs()
    {
        const string tools = "Build and change its graph with edit_node_graph and read it with read_node_graph, rather than through Model in apply_edit; get_node_graph_guide has the contract and examples.";
        s_descriptions[typeof(NodeGraphFilterEffect)] = $"An effect that a node graph computes, such as GLSL shader passes with several inputs and intermediate images, without C#. A new one holds an empty graph, which passes its layer through. {tools}";
        s_descriptions[typeof(NodeGraphDrawable)] = $"A drawable whose content a node graph makes. A new one holds an empty graph, which draws nothing. {tools}";
        s_descriptions[typeof(GraphModel)] = $"The node graph of a NodeGraphFilterEffect or a NodeGraphDrawable. {tools}";
    }

    private static void Register<T>(string format) => s_types.Add((format, typeof(T)));
}
