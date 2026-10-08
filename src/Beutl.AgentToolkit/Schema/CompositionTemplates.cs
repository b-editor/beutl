using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation.Easings;
using static Beutl.AgentToolkit.Schema.SamplePatchBuilder;

namespace Beutl.AgentToolkit.Schema;

public sealed record CompositionTemplateList(
    string Seed,
    IReadOnlyList<CompositionTemplateSummary> Compositions);

public sealed record CompositionTemplateSummary(
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> StyleAxes,
    IReadOnlyList<string> PropNames,
    CompositionMetadata DefaultMetadata);

public sealed record CompositionTemplateDetail(
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> StyleAxes,
    JsonObject DefaultProps,
    IReadOnlyList<CompositionPropDescriptor> Props,
    CompositionMetadata DefaultMetadata,
    IReadOnlyList<CompositionSequenceDescriptor> Sequences,
    IReadOnlyList<CompositionTransitionDescriptor> Transitions);

public sealed record CompositionPropDescriptor(
    string Name,
    string ValueType,
    object? Default,
    string Description);

public sealed record CompositionMetadata(
    string Id,
    int Width,
    int Height,
    int Fps,
    double DurationSeconds,
    int DurationInFrames,
    string Duration);

public sealed record CompositionSequenceDescriptor(
    string Name,
    int FromFrame,
    int DurationInFrames,
    string From,
    string Duration,
    string Layout,
    IReadOnlyList<string> Roles);

public sealed record CompositionTransitionDescriptor(
    string Name,
    string Type,
    int FromFrame,
    int DurationInFrames,
    string From,
    string Duration,
    string Easing);

public sealed record CompositionRender(
    string Name,
    string Seed,
    JsonObject InputProps,
    JsonObject ResolvedProps,
    CompositionMetadata Metadata,
    IReadOnlyList<CompositionSequenceDescriptor> Sequences,
    IReadOnlyList<CompositionTransitionDescriptor> Transitions,
    JsonObject Patch);

public sealed partial class CompositionTemplateCatalog
{
    private static readonly Lazy<CompositionTemplateSpec[]> s_templates = new(CreateTemplates);

    // Template patches lay elements out from t=0 of the visible window, but the document
    // stores absolute-axis Start values; a trimmed scene (Scene.Start > 0) needs every emitted
    // Start shifted into the window or the composition renders entirely off-screen.
    public static JsonObject OffsetPatchElementStarts(JsonObject patch, TimeSpan sceneStart)
    {
        if (sceneStart <= TimeSpan.Zero)
        {
            return patch;
        }

        var offsetPatch = (JsonObject)patch.DeepClone();
        if (offsetPatch["Elements"] is JsonArray elements)
        {
            foreach (JsonObject element in elements.OfType<JsonObject>())
            {
                TimeSpan start = element["Start"] is JsonValue value
                                 && value.TryGetValue(out string? text)
                                 && TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out TimeSpan parsed)
                    ? parsed
                    : TimeSpan.Zero;
                element["Start"] = (start + sceneStart).ToString("c");
            }
        }

        return offsetPatch;
    }

    private readonly string _defaultSeed;

    public CompositionTemplateCatalog(string? defaultSeed = null)
    {
        _defaultSeed = string.IsNullOrWhiteSpace(defaultSeed)
            ? CreateSeed("catalog")
            : defaultSeed.Trim();
    }

    public CompositionTemplateList List(
        string? tag = null,
        string? seed = null)
    {
        string resolvedSeed = ResolveSeed(seed);
        CompositionTemplateSummary[] summaries = s_templates.Value
            .Where(spec => MatchesTag(spec, tag))
            .Select(CreateSummary)
            .ToArray();

        return new CompositionTemplateList(resolvedSeed, Shuffle(summaries, resolvedSeed));
    }

    public CompositionTemplateDetail Get(string name)
    {
        CompositionTemplateSpec spec = Find(name);
        CompositionMetadata metadata = CalculateMetadata(spec.Name, spec.DefaultProps);
        return new CompositionTemplateDetail(
            spec.Name,
            spec.Description,
            spec.Tags.ToArray(),
            new Dictionary<string, string>(spec.StyleAxes, StringComparer.Ordinal),
            CloneObject(spec.DefaultProps),
            spec.Props.ToArray(),
            metadata,
            spec.CreateSequences(metadata),
            spec.CreateTransitions(metadata));
    }

    public CompositionRender Render(
        string? name = null,
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null)
    {
        string resolvedSeed = ResolveSeed(seed);
        CompositionTemplateSpec spec = string.IsNullOrWhiteSpace(name)
            ? PickFirst(tag, resolvedSeed)
            : Find(name);
        JsonObject input = inputProps is null ? [] : CloneObject(inputProps);
        JsonObject resolvedProps = MergeProps(spec.DefaultProps, input);
        CompositionMetadata metadata = CalculateMetadata(spec.Name, resolvedProps);
        CompositionContext context = new(
            spec,
            resolvedSeed,
            input,
            resolvedProps,
            metadata,
            spec.CreateSequences(metadata),
            spec.CreateTransitions(metadata));

        return spec.Render(context);
    }

    private static CompositionTemplateSpec PickFirst(string? tag, string seed)
    {
        CompositionTemplateSpec[] candidates = s_templates.Value
            .Where(spec => MatchesTag(spec, tag))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.UnknownType,
                $"No composition templates matched tag='{tag}'.",
                tag));
        }

        return Shuffle(candidates, seed)[0];
    }

    private static CompositionTemplateSpec Find(string name)
    {
        CompositionTemplateSpec? spec = s_templates.Value
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

        if (spec is null)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.UnknownType,
                $"No composition template named '{name}' exists.",
                name,
                "Call list_compositions to inspect available template names."));
        }

        return spec;
    }

    private static CompositionTemplateSummary CreateSummary(CompositionTemplateSpec spec)
    {
        return new CompositionTemplateSummary(
            spec.Name,
            spec.Description,
            spec.Tags.ToArray(),
            new Dictionary<string, string>(spec.StyleAxes, StringComparer.Ordinal),
            spec.Props.Select(prop => prop.Name).ToArray(),
            CalculateMetadata(spec.Name, spec.DefaultProps));
    }

    private static CompositionTemplateSpec[] CreateTemplates()
    {
        return
        [
            new CompositionTemplateSpec(
                "kinetic-ribbon-title",
                "Reusable composition template for a kinetic title reveal, seeded ribbon motion, noise dots, gradient fills, and effect chains.",
                ["starter", "empty-scene", "kinetic", "typography", "gradient", "noise"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["layoutFamily"] = "centered-title",
                    ["motionLanguage"] = "push-reveal",
                    ["palette"] = "seeded-electric",
                    ["effectMood"] = "glow-blur",
                    ["typography"] = "wide-display"
                },
                DefaultProps("Beutl motion", "Declarative composition with seeded variation"),
                SharedProps("Beutl motion", "Declarative composition with seeded variation"),
                CreateKineticSequences,
                CreateDefaultTransitions,
                RenderKineticRibbon),
            new CompositionTemplateSpec(
                "orbital-radar-map",
                "Reusable composition template for orbit rings, a radar sweep, seeded signal nodes, pens, glow effects, and calculated metadata.",
                ["starter", "empty-scene", "orbital", "radar", "rings", "pen", "glow"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["layoutFamily"] = "asymmetric-orbit",
                    ["motionLanguage"] = "scan-orbit",
                    ["palette"] = "seeded-instrument",
                    ["effectMood"] = "neon-shadow",
                    ["typography"] = "technical-label"
                },
                DefaultProps("Orbit map", "Signal route notes"),
                SharedProps("Orbit map", "Signal route notes"),
                CreateOrbitalSequences,
                CreateDefaultTransitions,
                RenderOrbitalRadar),
            new CompositionTemplateSpec(
                "split-screen-type-system",
                "Reusable composition template for split-screen editorial panels, seeded block layout, animated typography, gradients, and color effects.",
                ["starter", "empty-scene", "split-screen", "editorial", "blocks", "typography"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["layoutFamily"] = "split-screen",
                    ["motionLanguage"] = "panel-slide",
                    ["palette"] = "seeded-editorial",
                    ["effectMood"] = "saturated-shadow",
                    ["typography"] = "stacked-display"
                },
                DefaultProps("Frame flow", "Kinetic layout with restrained gradients"),
                SharedProps("Frame flow", "Kinetic layout with restrained gradients"),
                CreateSplitSequences,
                CreateDefaultTransitions,
                RenderSplitScreen),
            new CompositionTemplateSpec(
                "liquid-gradient-system",
                "Reusable composition template for liquid blobs, oversized gradient fields, soft focus, and drifting typography.",
                ["starter", "empty-scene", "liquid", "gradient", "organic", "soft-focus", "blob"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["layoutFamily"] = "organic-blob-field",
                    ["motionLanguage"] = "drift-morph",
                    ["palette"] = "seeded-liquid",
                    ["effectMood"] = "soft-saturate",
                    ["typography"] = "floating-title"
                },
                DefaultProps("Liquid signal", "Soft gradient field with drifting blobs"),
                SharedProps("Liquid signal", "Soft gradient field with drifting blobs"),
                CreateLiquidSequences,
                CreateDefaultTransitions,
                RenderLiquidGradient),
            new CompositionTemplateSpec(
                "data-bar-dashboard",
                "Reusable composition template for animated metric bars, dense data strips, dashboard labels, and editorial color grading.",
                ["starter", "empty-scene", "data", "dashboard", "bars", "metrics", "editorial"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["layoutFamily"] = "dashboard-bars",
                    ["motionLanguage"] = "metric-rise",
                    ["palette"] = "seeded-analytics",
                    ["effectMood"] = "high-contrast-grade",
                    ["typography"] = "metric-label"
                },
                DefaultProps("Signal index", "Editorial data system"),
                SharedProps("Signal index", "Editorial data system"),
                CreateDataSequences,
                CreateDefaultTransitions,
                RenderDataDashboard),
            new CompositionTemplateSpec(
                "glitch-cutout-collage",
                "Reusable composition template for glitch slices, cutout panels, chromatic shifts, pixel sampling, and hard title cuts.",
                ["starter", "empty-scene", "glitch", "collage", "cutout", "chromatic", "pixel"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["layoutFamily"] = "cutout-collage",
                    ["motionLanguage"] = "jump-cut-slice",
                    ["palette"] = "seeded-glitch",
                    ["effectMood"] = "chromatic-pixel",
                    ["typography"] = "hard-cut"
                },
                DefaultProps("Glitch cut", "Chromatic collage with pixel slices"),
                SharedProps("Glitch cut", "Chromatic collage with pixel slices"),
                CreateGlitchSequences,
                CreateDefaultTransitions,
                RenderGlitchCollage)
        ];
    }

    private static JsonObject DefaultProps(string title, string subtitle)
    {
        return new JsonObject
        {
            ["title"] = title,
            ["subtitle"] = subtitle,
            ["width"] = 1920,
            ["height"] = 1080,
            ["fps"] = 30,
            ["durationSeconds"] = 8,
            ["density"] = 1,
            ["intensity"] = 1
        };
    }

    private static CompositionPropDescriptor[] SharedProps(string title, string subtitle)
    {
        return
        [
            new("title", "string", title, "Main text passed like Remotion inputProps."),
            new("subtitle", "string", subtitle, "Secondary text passed like Remotion inputProps."),
            new("width", "integer", 1920, "Composition width used by calculateMetadata."),
            new("height", "integer", 1080, "Composition height used by calculateMetadata."),
            new("fps", "integer", 30, "Frame rate used by calculateMetadata and Sequence frame math."),
            new("durationSeconds", "number", 8, "Composition duration used by calculateMetadata."),
            new("density", "number", 1, "Seeded object density multiplier."),
            new("intensity", "number", 1, "Motion and effect intensity multiplier."),
            new("backgroundA", "color", null, "Optional ARGB color override. If omitted, seed selects a palette."),
            new("backgroundB", "color", null, "Optional ARGB color override. If omitted, seed selects a palette."),
            new("accent", "color", null, "Optional ARGB accent color override. If omitted, seed selects a palette."),
            new("secondaryAccent", "color", null, "Optional ARGB secondary accent color override. If omitted, seed selects a palette."),
            new("foreground", "color", null, "Optional ARGB text color override. If omitted, seed selects a palette.")
        ];
    }

    private static CompositionSequenceDescriptor[] CreateKineticSequences(CompositionMetadata metadata)
    {
        return CreateSequences(metadata, "absolute-fill", "ribbon-motion", "title-lockup");
    }

    private static CompositionSequenceDescriptor[] CreateOrbitalSequences(CompositionMetadata metadata)
    {
        return CreateSequences(metadata, "absolute-fill", "orbital-scan", "technical-labels");
    }

    private static CompositionSequenceDescriptor[] CreateSplitSequences(CompositionMetadata metadata)
    {
        return CreateSequences(metadata, "absolute-fill", "panel-slide", "type-system");
    }

    private static CompositionSequenceDescriptor[] CreateLiquidSequences(CompositionMetadata metadata)
    {
        return CreateSequences(metadata, "organic-blob-field", "blob-drift", "floating-title");
    }

    private static CompositionSequenceDescriptor[] CreateDataSequences(CompositionMetadata metadata)
    {
        return CreateSequences(metadata, "dashboard-bars", "metric-rise", "metric-labels");
    }

    private static CompositionSequenceDescriptor[] CreateGlitchSequences(CompositionMetadata metadata)
    {
        return CreateSequences(metadata, "cutout-collage", "slice-jump", "hard-cut-type");
    }

    private static CompositionSequenceDescriptor[] CreateSequences(CompositionMetadata metadata, string layout, string bodyRole, string textRole)
    {
        int intro = Math.Clamp(metadata.Fps, 12, Math.Max(12, metadata.DurationInFrames / 3));
        int outro = intro;
        int bodyFrom = intro / 2;
        int bodyDuration = Math.Max(1, metadata.DurationInFrames - bodyFrom);
        int textFrom = Math.Max(1, (int)Math.Round(metadata.Fps * 0.8));
        int textDuration = Math.Max(1, metadata.DurationInFrames - textFrom);

        return
        [
            CreateSequence("intro", 0, intro, metadata, layout, ["background", "reveal"]),
            CreateSequence("body", bodyFrom, bodyDuration, metadata, layout, ["background", bodyRole]),
            CreateSequence("typography", textFrom, textDuration, metadata, layout, [textRole]),
            CreateSequence("outro", Math.Max(0, metadata.DurationInFrames - outro), outro, metadata, layout, ["fade-out"])
        ];
    }

    private static CompositionTransitionDescriptor[] CreateDefaultTransitions(CompositionMetadata metadata)
    {
        int transitionFrames = Math.Clamp(metadata.Fps / 2, 8, 24);
        return
        [
            CreateTransition("intro-to-body", "opacity+translate", Math.Max(0, metadata.Fps - transitionFrames), transitionFrames, metadata, nameof(CubicEaseOut)),
            CreateTransition("body-to-outro", "opacity", Math.Max(0, metadata.DurationInFrames - transitionFrames), transitionFrames, metadata, nameof(SineEaseInOut))
        ];
    }

    private static CompositionSequenceDescriptor CreateSequence(string name, int fromFrame, int durationInFrames, CompositionMetadata metadata, string layout, string[] roles)
    {
        return new CompositionSequenceDescriptor(
            name,
            fromFrame,
            durationInFrames,
            FrameToTime(fromFrame, metadata.Fps),
            FrameToTime(durationInFrames, metadata.Fps),
            layout,
            roles);
    }

    private static CompositionTransitionDescriptor CreateTransition(string name, string type, int fromFrame, int durationInFrames, CompositionMetadata metadata, string easing)
    {
        return new CompositionTransitionDescriptor(
            name,
            type,
            fromFrame,
            durationInFrames,
            FrameToTime(fromFrame, metadata.Fps),
            FrameToTime(durationInFrames, metadata.Fps),
            easing);
    }

    private static CompositionMetadata CalculateMetadata(string id, JsonObject props)
    {
        int width = Math.Clamp(ReadInt(props, "width", 1920), 16, 16384);
        int height = Math.Clamp(ReadInt(props, "height", 1080), 16, 16384);
        int fps = Math.Clamp(ReadInt(props, "fps", 30), 1, 240);
        double durationSeconds = Math.Clamp(ReadDouble(props, "durationSeconds", 8), 0.1, 3600);
        int durationInFrames = Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));
        return new CompositionMetadata(
            id,
            width,
            height,
            fps,
            durationSeconds,
            durationInFrames,
            TimeSpan.FromSeconds(durationSeconds).ToString("c"));
    }

    private static JsonObject MergeProps(JsonObject defaults, JsonObject input)
    {
        JsonObject result = CloneObject(defaults);
        foreach ((string key, JsonNode? value) in input)
        {
            result[key] = value?.DeepClone();
        }

        return result;
    }

    private string ResolveSeed(string? seed)
    {
        return string.IsNullOrWhiteSpace(seed)
            ? _defaultSeed
            : seed.Trim();
    }

    private static string CreateSeed(string scope)
    {
        return $"{scope}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
    }

    private static T[] Shuffle<T>(IReadOnlyList<T> source, string seed)
    {
        T[] items = source.ToArray();
        var random = new SeededValues($"{seed}:shuffle");
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = random.NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }

    private static bool MatchesTag(CompositionTemplateSpec spec, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return true;
        }

        string[] tokens = SearchTokens(tag);
        return tokens.Length == 0
               || tokens.Any(token =>
                   spec.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
                   || spec.Description.Contains(token, StringComparison.OrdinalIgnoreCase)
                   || spec.Tags.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase))
                   || spec.StyleAxes.Any(axis =>
                       axis.Key.Contains(token, StringComparison.OrdinalIgnoreCase)
                       || axis.Value.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static JsonObject CloneObject(JsonObject value)
    {
        return (JsonObject)value.DeepClone();
    }

    private static string FrameToTime(int frame, int fps)
    {
        return TimeSpan.FromSeconds(frame / (double)fps).ToString("c");
    }

    private static string ReadString(JsonObject props, string name, string fallback)
    {
        return props.TryGetPropertyValue(name, out JsonNode? node) && node is not null
            ? node.GetValue<string>()
            : fallback;
    }

    private static int ReadInt(JsonObject props, string name, int fallback)
    {
        return props.TryGetPropertyValue(name, out JsonNode? node) && TryReadDouble(node, out double value)
            ? (int)Math.Round(value)
            : fallback;
    }

    private static float ReadFloat(JsonObject props, string name, float fallback)
    {
        return props.TryGetPropertyValue(name, out JsonNode? node) && TryReadDouble(node, out double value)
            ? (float)value
            : fallback;
    }

    private static double ReadDouble(JsonObject props, string name, double fallback)
    {
        return props.TryGetPropertyValue(name, out JsonNode? node) && TryReadDouble(node, out double value)
            ? value
            : fallback;
    }

    private static bool TryReadDouble(JsonNode? node, out double value)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue(out double doubleValue))
            {
                value = doubleValue;
                return true;
            }

            if (jsonValue.TryGetValue(out int intValue))
            {
                value = intValue;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static ulong StableHash(string text)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong hash = offset;
        foreach (byte value in Encoding.UTF8.GetBytes(text))
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }

    private sealed record CompositionTemplateSpec(
        string Name,
        string Description,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, string> StyleAxes,
        JsonObject DefaultProps,
        IReadOnlyList<CompositionPropDescriptor> Props,
        Func<CompositionMetadata, CompositionSequenceDescriptor[]> CreateSequences,
        Func<CompositionMetadata, CompositionTransitionDescriptor[]> CreateTransitions,
        Func<CompositionContext, CompositionRender> Render);

    private sealed record CompositionContext(
        CompositionTemplateSpec Spec,
        string Seed,
        JsonObject InputProps,
        JsonObject ResolvedProps,
        CompositionMetadata Metadata,
        IReadOnlyList<CompositionSequenceDescriptor> Sequences,
        IReadOnlyList<CompositionTransitionDescriptor> Transitions)
    {
        public SeededValues Random { get; } = new($"{Seed}:{Spec.Name}");
    }

    private sealed class SeededValues
    {
        private readonly string _seed;
        private ulong _state;

        public SeededValues(string seed)
        {
            _seed = seed;
            _state = StableHash(seed);
            if (_state == 0)
            {
                _state = 0x9e3779b97f4a7c15;
            }
        }

        public int NextInt(int maxExclusive)
        {
            return Math.Clamp((int)(NextDouble() * maxExclusive), 0, maxExclusive - 1);
        }

        public float Range(float minimum, float maximum)
        {
            return minimum + ((float)NextDouble() * (maximum - minimum));
        }

        public double Noise(string channel, int index)
        {
            var random = new SeededValues($"{_seed}:{channel}:{index}");
            return (random.NextDouble() * 2) - 1;
        }

        private double NextDouble()
        {
            ulong x = _state;
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            _state = x;
            return (x >> 11) * (1.0 / (1UL << 53));
        }
    }
}
