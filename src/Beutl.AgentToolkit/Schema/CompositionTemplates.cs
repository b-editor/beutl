using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

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

public sealed class CompositionTemplateCatalog
{
    private static readonly Lazy<CompositionTemplateSpec[]> s_templates = new(CreateTemplates);

    private static readonly Palette[] s_palettes =
    [
        new("#ff06121f", "#ff123f63", "#ff20d6ff", "#ffffe28a", "#ffffffff"),
        new("#ff020711", "#ff063835", "#ff43e7ff", "#ffffd36b", "#ffc9faff"),
        new("#ff071225", "#ff2e1446", "#ffff4aa8", "#ff34e6ff", "#ffffffff"),
        new("#ff11151d", "#ff243b38", "#ffb8ff5c", "#ff6cf3ff", "#fff7fff6"),
        new("#ff160c24", "#ff302340", "#ffff7a59", "#ff63f7de", "#fffff8ef")
    ];

    private static readonly (string Name, string[] Tokens)[] s_templateInferenceTokens =
    [
        ("kinetic-ribbon-title", ["kinetic-ribbon-title", "create-empty-scene-motion-graphics", "kinetic ribbon", "beutl motion", "seeded ribbon"]),
        ("orbital-radar-map", ["orbital-radar-map", "create-empty-scene-orbital-radar", "orbital radar", "orbit map", "seeded orbit", "signal atlas"]),
        ("split-screen-type-system", ["split-screen-type-system", "create-empty-scene-split-screen-typography", "split screen", "frame flow", "seeded panel"]),
        ("liquid-gradient-system", ["liquid-gradient-system", "liquid signal", "seeded liquid", "blob field"]),
        ("data-bar-dashboard", ["data-bar-dashboard", "signal index", "seeded metric", "dashboard"]),
        ("glitch-cutout-collage", ["glitch-cutout-collage", "glitch cut", "seeded glitch", "chromatic collage"])
    ];

    private readonly string _defaultSeed;

    public CompositionTemplateCatalog(string? defaultSeed = null)
    {
        _defaultSeed = string.IsNullOrWhiteSpace(defaultSeed)
            ? CreateSeed("catalog")
            : defaultSeed.Trim();
    }

    public CompositionTemplateList List(
        string? tag = null,
        string? seed = null,
        IReadOnlyList<string>? deprioritizedNames = null)
    {
        string resolvedSeed = ResolveSeed(seed);
        CompositionTemplateSummary[] summaries = s_templates.Value
            .Where(spec => MatchesTag(spec, tag))
            .Select(CreateSummary)
            .ToArray();

        return new CompositionTemplateList(resolvedSeed, Deprioritize(Shuffle(summaries, resolvedSeed), deprioritizedNames));
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
        string? seed = null,
        IReadOnlyList<string>? deprioritizedNames = null,
        bool enforceFirstSelection = false)
    {
        string resolvedSeed = ResolveSeed(seed);
        CompositionTemplateSpec spec = string.IsNullOrWhiteSpace(name)
            ? PickFirst(tag, resolvedSeed, deprioritizedNames)
            : Find(name);
        if (!string.IsNullOrWhiteSpace(name) && IsDeprioritized(spec.Name, deprioritizedNames))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Composition template '{spec.Name}' was recently used or previewed.",
                spec.Name,
                "Call list_compositions and choose a different non-avoided returned template name only when the user explicitly asks for a reusable template/starter, or pass avoidRecent=false only when the user intentionally asks to repeat this style."));
        }

        if (enforceFirstSelection && !string.IsNullOrWhiteSpace(name))
        {
            CompositionTemplateSummary? first = List(tag, resolvedSeed, deprioritizedNames).Compositions.FirstOrDefault();
            if (first is null)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"No composition template matched tag '{tag}'.",
                    tag,
                    "Call list_compositions without this tag, or choose a tag returned by get_composition/list_compositions."));
            }

            if (!string.Equals(first.Name, spec.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Composition template '{spec.Name}' is not allowed by the current template rotation.",
                    spec.Name,
                    $"Choose '{first.Name}' from list_compositions when you need the default template rotation, or pass avoidRecent=false only when the user intentionally asks for '{spec.Name}'. For original creative briefs, use plan_edit/apply_edit with a custom patch instead."));
            }
        }

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

    public static string? TryInferTemplateName(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        string text = node.ToJsonString();
        foreach ((string name, string[] tokens) in s_templateInferenceTokens)
        {
            if (tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return null;
    }

    public static string? TryInferTemplateNameFromExampleName(string exampleName)
    {
        foreach ((string name, string[] tokens) in s_templateInferenceTokens)
        {
            if (tokens.Any(token => exampleName.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return null;
    }

    private static CompositionTemplateSpec PickFirst(string? tag, string seed, IReadOnlyList<string>? deprioritizedNames)
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

        return Deprioritize(Shuffle(candidates, seed), deprioritizedNames)[0];
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
                DefaultProps("BEUTL MOTION", "DECLARATIVE COMPOSITION / SEEDED VARIATION"),
                SharedProps("BEUTL MOTION", "DECLARATIVE COMPOSITION / SEEDED VARIATION"),
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
                DefaultProps("ORBIT MAP", "SIGNAL ROUTES / SEEDED DECLARATIVE EDIT"),
                SharedProps("ORBIT MAP", "SIGNAL ROUTES / SEEDED DECLARATIVE EDIT"),
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
                DefaultProps("FRAME FLOW", "KINETIC LAYOUT / GRADIENT BRUSHES / EFFECT CHAIN"),
                SharedProps("FRAME FLOW", "KINETIC LAYOUT / GRADIENT BRUSHES / EFFECT CHAIN"),
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
                DefaultProps("LIQUID SIGNAL", "SOFT GRADIENT FIELD / DRIFTING BLOBS"),
                SharedProps("LIQUID SIGNAL", "SOFT GRADIENT FIELD / DRIFTING BLOBS"),
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
                DefaultProps("SIGNAL INDEX", "LIVE METRICS / EDITORIAL DATA SYSTEM"),
                SharedProps("SIGNAL INDEX", "LIVE METRICS / EDITORIAL DATA SYSTEM"),
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
                DefaultProps("GLITCH CUT", "CHROMATIC COLLAGE / PIXEL SLICE SYSTEM"),
                SharedProps("GLITCH CUT", "CHROMATIC COLLAGE / PIXEL SLICE SYSTEM"),
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

    private static CompositionRender RenderKineticRibbon(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 0);
        string title = ReadString(context.ResolvedProps, "title", "BEUTL MOTION");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "DECLARATIVE COMPOSITION");
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Kinetic ribbon background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Seeded gradient field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) }
                })
        ];

        for (int i = 0; i < Math.Clamp((int)MathF.Round(3 * density), 2, 6); i++)
        {
            float y = -170 + (i * 140) + context.Random.Range(-36, 36);
            float rotation = -15 + (i * 6) + context.Random.Range(-6, 6);
            float startX = -780 + context.Random.Range(-140, 120);
            float endX = 620 + context.Random.Range(-160, 180);
            Element ribbon = CreateElement(
                $"Kinetic ribbon band {i + 1}",
                4 + i,
                fullLength,
                new RectShape
                {
                    Name = $"Seeded ribbon {i + 1}",
                    Width = { CurrentValue = 1060 + context.Random.Range(-120, 180) },
                    Height = { CurrentValue = 58 + context.Random.Range(0, 34) },
                    Fill = { CurrentValue = CreateLinearGradient(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, i % 2 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(startX, y),
                                new RotationTransform(rotation)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur(2.5f * intensity),
                                CreateBrightness(106 + (8 * intensity)),
                                CreateDropShadow(0, 0, 14 * intensity, "#9936f0ff")
                            }
                        }
                    }
                });

            JsonObject ribbonJson = SerializeElement(ribbon);
            JsonObject ribbonObject = GetFirstObjectJson(ribbonJson);
            AddFloatAnimation(ribbonObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7 + (i * 0.12), 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 80, typeof(SineEaseInOut)));
            AddFloatAnimation(GetTransformChildJson(ribbonObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, startX, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endX, typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(ribbonJson));
        }

        Element titleElement = CreateElement(
            "Kinetic ribbon title",
            30,
            fullLength,
            new TextBlock
            {
                Name = "Seeded title",
                Text = { CurrentValue = title },
                Size = { CurrentValue = 104 + context.Random.Range(-10, 8) },
                Spacing = { CurrentValue = 8 + context.Random.Range(0, 8) },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse(palette.Foreground)) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(context.Random.Range(-80, 80), -20 + context.Random.Range(-28, 26))
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(10, 16, 12, "#99000000")
                        }
                    }
                }
            });
        JsonObject titleJson = SerializeElement(titleElement);
        JsonObject titleObject = GetFirstObjectJson(titleJson);
        AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.9, 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds - 0.6, 100, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 22, typeof(CubicEaseOut)), (1.4, 8, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 14, typeof(SineEaseInOut)));
        elements.Add(DeserializeElement(titleJson));

        elements.Add(CreateTextElement("Kinetic ribbon subtitle", "Seeded subtitle", subtitle, 31, 32, 5, 0, 82, palette.Foreground, fullLength));
        AddNoiseDots(elements, context, palette, fullLength, zStart: 16, count: Math.Clamp((int)MathF.Round(12 * density), 6, 24));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderOrbitalRadar(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 1);
        string title = ReadString(context.ResolvedProps, "title", "ORBIT MAP");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "SIGNAL ROUTES");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Orbital composition background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Instrument gradient field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) }
                })
        ];

        int layoutVariant = context.Random.NextInt(3);
        float centerBaseX = layoutVariant switch
        {
            1 => 250,
            2 => -20,
            _ => -250
        };
        float centerBaseY = layoutVariant switch
        {
            2 => -120,
            _ => 0
        };
        float titleBaseX = layoutVariant switch
        {
            1 => -520,
            2 => -420,
            _ => 420
        };
        float titleBaseY = layoutVariant == 2 ? 300 : -72;
        int ringCount = layoutVariant == 2 ? 4 : 3;
        float ringBaseSize = layoutVariant == 2 ? 300 : 360;
        float ringGap = layoutVariant == 2 ? 150 : 190;
        float centerX = centerBaseX + context.Random.Range(-140, 80);
        float centerY = centerBaseY + context.Random.Range(-70, 60);
        for (int i = 0; i < ringCount; i++)
        {
            float size = ringBaseSize + (i * ringGap) + context.Random.Range(-34, 44);
            Element ring = CreateElement(
                $"Orbital radar ring {i + 1}",
                4 + i,
                fullLength,
                new EllipseShape
                {
                    Name = $"Seeded orbit ring {i + 1}",
                    Width = { CurrentValue = size },
                    Height = { CurrentValue = size },
                    Fill = { CurrentValue = null },
                    Pen = { CurrentValue = CreatePen(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, 3 + i) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(centerX, centerY),
                                new RotationTransform(context.Random.Range(-20, 20))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur(0.5f + (i * 0.5f)),
                                CreateDropShadow(0, 0, (12 + (i * 5)) * intensity, "#aa43e7ff")
                            }
                        }
                    }
                });

            JsonObject ringJson = SerializeElement(ring);
            JsonObject ringObject = GetFirstObjectJson(ringJson);
            AddFloatAnimation(ringObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.5 + (i * 0.18), 92 - (i * 12), typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 45 + (i * 8), typeof(SineEaseInOut)));
            AddFloatAnimation(GetTransformChildJson(ringObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, i * 16, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, (i % 2 == 0 ? 360 : -260), typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(ringJson));
        }

        float sweepWidth = layoutVariant switch
        {
            1 => 760,
            2 => 1120,
            _ => 820
        };
        float sweepStartRotation = layoutVariant switch
        {
            1 => 150,
            2 => -70,
            _ => -20
        };
        float sweepEndRotation = sweepStartRotation + (layoutVariant == 1 ? -260 : 260);
        Element sweep = CreateElement(
            "Orbital radar sweep",
            10,
            fullLength,
            new RectShape
            {
                Name = "Seeded scan sweep",
                Width = { CurrentValue = sweepWidth },
                Height = { CurrentValue = 9 },
                Fill = { CurrentValue = CreateLinearGradient("#0036f0ff", palette.Accent) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(centerX, centerY),
                            new RotationTransform(sweepStartRotation)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(2.2f * intensity)
                        }
                    }
                }
            });
        JsonObject sweepJson = SerializeElement(sweep);
        AddFloatAnimation(GetFirstObjectJson(sweepJson), nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.8, 78, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(GetFirstObjectJson(sweepJson), typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, sweepStartRotation, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, sweepEndRotation, typeof(SineEaseInOut)));
        elements.Add(DeserializeElement(sweepJson));

        int nodeCount = Math.Clamp((int)MathF.Round((layoutVariant == 2 ? 5 : 4) * density), 2, 9);
        float nodeSpreadX = layoutVariant == 2 ? 520 : 420;
        float nodeSpreadY = layoutVariant == 2 ? 250 : 300;
        for (int i = 0; i < nodeCount; i++)
        {
            float x = centerX + context.Random.Range(-nodeSpreadX, nodeSpreadX);
            float y = centerY + context.Random.Range(-nodeSpreadY, nodeSpreadY);
            Element node = CreateElement(
                $"Orbital signal node {i + 1}",
                14 + i,
                fullLength,
                new EllipseShape
                {
                    Name = $"Seeded signal node {i + 1}",
                    Width = { CurrentValue = 48 + context.Random.Range(0, 38) },
                    Height = { CurrentValue = 48 + context.Random.Range(0, 38) },
                    Fill = { CurrentValue = CreateLinearGradient(palette.SecondaryAccent, palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateDropShadow(0, 0, 20 * intensity, "#cc36f0ff"),
                                CreateBrightness(116)
                            }
                        }
                    }
                });

            JsonObject nodeJson = SerializeElement(node);
            JsonObject nodeObject = GetFirstObjectJson(nodeJson);
            AddFloatAnimation(nodeObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.6 + (i * 0.2), 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds - 0.3, 100, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
            JsonObject translate = GetTransformChildJson(nodeObject, typeof(TranslateTransform));
            AddFloatAnimation(translate, nameof(TranslateTransform.X), (0, x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, x + context.Random.Range(-120, 120), typeof(SineEaseInOut)));
            AddFloatAnimation(translate, nameof(TranslateTransform.Y), (0, y, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, y + context.Random.Range(-120, 120), typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(nodeJson));
        }

        float titleX = Math.Clamp(titleBaseX + context.Random.Range(-20, 80), -560, 560);
        elements.Add(CreateTextElement("Orbital title", "Technical title", title, 30, 88, 10, titleX, titleBaseY, palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Orbital subtitle", "Technical subtitle", subtitle, 31, 32, 5, titleX, titleBaseY + 92, palette.Foreground, fullLength));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderSplitScreen(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 2);
        string title = ReadString(context.ResolvedProps, "title", "FRAME FLOW");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "KINETIC LAYOUT");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Split composition background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Editorial gradient field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateSaturate(110 + (8 * intensity)),
                                CreateHueRotate(context.Random.Range(-8, 8))
                            }
                        }
                    }
                })
        ];

        float panelX = -410 + context.Random.Range(-90, 110);
        Element panel = CreateElement(
            "Split screen panel",
            4,
            fullLength,
            new RoundedRectShape
            {
                Name = "Seeded editorial panel",
                Width = { CurrentValue = 700 + context.Random.Range(-70, 110) },
                Height = { CurrentValue = 580 + context.Random.Range(-60, 80) },
                CornerRadius = { CurrentValue = new CornerRadius(48 + context.Random.Range(-12, 14)) },
                Fill = { CurrentValue = CreateLinearGradient("#eeffffff", palette.SecondaryAccent) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(panelX - 220, 0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(0.8f),
                            CreateDropShadow(24, 34, 18 * intensity, "#99000000")
                        }
                    }
                }
            });
        JsonObject panelJson = SerializeElement(panel);
        JsonObject panelObject = GetFirstObjectJson(panelJson);
        AddFloatAnimation(panelObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7, 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 100, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(panelObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, panelX - 260, typeof(CubicEaseOut)), (1.1, panelX, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, panelX + 38, typeof(SineEaseInOut)));
        elements.Add(DeserializeElement(panelJson));

        elements.Add(CreateTextElement("Split screen headline", "Stacked headline", title, 16, 92 + context.Random.Range(-12, 10), 4, panelX, -72, "#ff081225", fullLength));
        elements.Add(CreateTextElement("Split screen caption", "Panel caption", subtitle, 17, 28, 3, panelX, 48, "#ff10223c", fullLength));

        for (int i = 0; i < 4; i++)
        {
            float x = 310 + (i * 120) + context.Random.Range(-80, 80);
            float y = -250 + (i * 135) + context.Random.Range(-50, 50);
            Element block = CreateElement(
                $"Split screen block {i + 1}",
                8 + i,
                fullLength,
                new RectShape
                {
                    Name = $"Seeded editorial block {i + 1}",
                    Width = { CurrentValue = i % 2 == 0 ? 610 + context.Random.Range(-60, 90) : 82 + context.Random.Range(-12, 24) },
                    Height = { CurrentValue = i % 2 == 0 ? 86 + context.Random.Range(-10, 24) : 420 + context.Random.Range(-60, 90) },
                    Fill = { CurrentValue = CreateLinearGradient(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, i % 2 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x + 260, y)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBrightness(112),
                                CreateDropShadow(18, 20, 14 * intensity, "#88000000")
                            }
                        }
                    }
                });

            JsonObject blockJson = SerializeElement(block);
            JsonObject blockObject = GetFirstObjectJson(blockJson);
            AddFloatAnimation(blockObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.8 + (i * 0.16), 96, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 78, typeof(SineEaseInOut)));
            AddFloatAnimation(GetTransformChildJson(blockObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, x + 320, typeof(CubicEaseOut)), (1.2 + (i * 0.18), x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, x - 80, typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(blockJson));
        }

        elements.Add(CreateTextElement("Split screen variant label", "Variant label", $"VARIANT {(int)(StableHash(context.Seed) % 97):00}", 30, 50, 10, 520, 96, palette.Foreground, fullLength));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderLiquidGradient(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 3);
        string title = ReadString(context.ResolvedProps, "title", "LIQUID SIGNAL");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "SOFT GRADIENT FIELD");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Liquid gradient background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Soft liquid field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateSaturate(118 + (8 * intensity)),
                                CreateBrightness(104)
                            }
                        }
                    }
                })
        ];

        int blobCount = Math.Clamp((int)MathF.Round(5 * density), 4, 9);
        for (int i = 0; i < blobCount; i++)
        {
            float size = 260 + context.Random.Range(0, 260);
            float x = context.Random.Range(-760, 760);
            float y = context.Random.Range(-340, 340);
            float endX = x + context.Random.Range(-220, 220);
            float endY = y + context.Random.Range(-180, 180);
            Element blob = CreateElement(
                $"Liquid gradient blob {i + 1}",
                3 + i,
                fullLength,
                new EllipseShape
                {
                    Name = $"Seeded liquid blob {i + 1}",
                    Width = { CurrentValue = size },
                    Height = { CurrentValue = size * context.Random.Range(0.62f, 1.18f) },
                    Fill = { CurrentValue = CreateRadialGradient(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent, "#00111111") },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y),
                                new RotationTransform(context.Random.Range(-24, 24))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur((6 + (i * 1.2f)) * intensity),
                                CreateDropShadow(0, 0, 28 * intensity, i % 2 == 0 ? "#7734e6ff" : "#77ff7a59")
                            }
                        }
                    }
                });

            JsonObject blobJson = SerializeElement(blob);
            JsonObject blobObject = GetFirstObjectJson(blobJson);
            AddFloatAnimation(blobObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.5 + (i * 0.1), 84, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 54, typeof(SineEaseInOut)));
            JsonObject translate = GetTransformChildJson(blobObject, typeof(TranslateTransform));
            AddFloatAnimation(translate, nameof(TranslateTransform.X), (0, x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endX, typeof(SineEaseInOut)));
            AddFloatAnimation(translate, nameof(TranslateTransform.Y), (0, y, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endY, typeof(SineEaseInOut)));
            AddFloatAnimation(GetTransformChildJson(blobObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, context.Random.Range(-18, 18), typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, context.Random.Range(90, 220), typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(blobJson));
        }

        elements.Add(CreateTextElement("Liquid title", "Floating title", title, 30, 84, 7, -620 + context.Random.Range(-60, 90), 245 + context.Random.Range(-40, 30), palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Liquid subtitle", "Floating subtitle", subtitle, 31, 30, 4, -618 + context.Random.Range(-60, 90), 338, palette.Foreground, fullLength));
        AddNoiseDots(elements, context, palette, fullLength, zStart: 18, count: Math.Clamp((int)MathF.Round(10 * density), 6, 20));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderDataDashboard(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 4);
        string title = ReadString(context.ResolvedProps, "title", "SIGNAL INDEX");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "LIVE METRICS");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Dashboard background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Editorial dashboard field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateHighContrast(8 + (6 * intensity)),
                                CreateSaturate(122)
                            }
                        }
                    }
                })
        ];

        for (int i = 0; i < 7; i++)
        {
            float y = -300 + (i * 100);
            Element rule = CreateElement(
                $"Dashboard scanline {i + 1}",
                2,
                fullLength,
                new RectShape
                {
                    Name = $"Metric rule {i + 1}",
                    Width = { CurrentValue = 1380 },
                    Height = { CurrentValue = 2 },
                    Fill = { CurrentValue = new SolidColorBrush(Color.Parse(i % 2 == 0 ? "#33ffffff" : "#2243e7ff")) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(120, y)
                            }
                        }
                    }
                });
            elements.Add(rule);
        }

        int barCount = Math.Clamp((int)MathF.Round(12 * density), 8, 18);
        for (int i = 0; i < barCount; i++)
        {
            float targetHeight = 140 + (float)((context.Random.Noise("bar-height", i) + 1) * 190);
            float x = -620 + (i * (1120f / Math.Max(1, barCount - 1))) + context.Random.Range(-18, 18);
            float y = 220 - (targetHeight / 2);
            Element bar = CreateElement(
                $"Dashboard metric bar {i + 1}",
                5 + i,
                fullLength,
                new RoundedRectShape
                {
                    Name = $"Seeded metric bar {i + 1}",
                    Width = { CurrentValue = 46 + context.Random.Range(-8, 16) },
                    Height = { CurrentValue = 20 },
                    CornerRadius = { CurrentValue = new CornerRadius(18) },
                    Fill = { CurrentValue = CreateLinearGradient(i % 3 == 0 ? palette.Accent : palette.SecondaryAccent, i % 3 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y + (targetHeight / 2))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateDropShadow(0, 10, 16 * intensity, "#77000000"),
                                CreateBrightness(110)
                            }
                        }
                    }
                });

            JsonObject barJson = SerializeElement(bar);
            JsonObject barObject = GetFirstObjectJson(barJson);
            AddFloatAnimation(barObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.35 + (i * 0.05), 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 82, typeof(SineEaseInOut)));
            AddFloatAnimation(barObject, nameof(RectShape.Height), (0, 20, typeof(CubicEaseOut)), (0.65 + (i * 0.04), targetHeight, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, targetHeight + context.Random.Range(-80, 80), typeof(SineEaseInOut)));
            JsonObject translate = GetTransformChildJson(barObject, typeof(TranslateTransform));
            AddFloatAnimation(translate, nameof(TranslateTransform.Y), (0, y + (targetHeight / 2), typeof(CubicEaseOut)), (0.65 + (i * 0.04), y, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, y + context.Random.Range(-30, 30), typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(barJson));
        }

        elements.Add(CreateTextElement("Dashboard title", "Metric title", title, 30, 76, 6, -710, -360, palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Dashboard subtitle", "Metric subtitle", subtitle, 31, 28, 4, -708, -270, palette.Foreground, fullLength));
        elements.Add(CreateTextElement("Dashboard value label", "Metric value", $"{(int)(StableHash(context.Seed) % 9000) + 1000}", 32, 96, 2, 520, -335, palette.Accent, fullLength));

        return CreateRender(context, elements);
    }

    private static CompositionRender RenderGlitchCollage(CompositionContext context)
    {
        Palette palette = ResolvePalette(context, offset: 5);
        string title = ReadString(context.ResolvedProps, "title", "GLITCH CUT");
        string subtitle = ReadString(context.ResolvedProps, "subtitle", "CHROMATIC COLLAGE");
        float intensity = ReadFloat(context.ResolvedProps, "intensity", 1);
        float density = ReadFloat(context.ResolvedProps, "density", 1);
        TimeSpan fullLength = TimeSpan.FromSeconds(context.Metadata.DurationSeconds);

        List<Element> elements =
        [
            CreateElement(
                "Glitch collage background",
                0,
                fullLength,
                new RectShape
                {
                    Name = "Chromatic cutout field",
                    Width = { CurrentValue = context.Metadata.Width },
                    Height = { CurrentValue = context.Metadata.Height },
                    Fill = { CurrentValue = CreateLinearGradient(palette.BackgroundA, palette.BackgroundB) },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateHighContrast(12),
                                CreateColorShift((int)(4 * intensity))
                            }
                        }
                    }
                })
        ];

        int sliceCount = Math.Clamp((int)MathF.Round(8 * density), 6, 14);
        for (int i = 0; i < sliceCount; i++)
        {
            float width = 280 + context.Random.Range(80, 420);
            float height = 34 + context.Random.Range(20, 110);
            float x = context.Random.Range(-720, 720);
            float y = context.Random.Range(-310, 290);
            float endX = x + context.Random.Range(-260, 260);
            Element slice = CreateElement(
                $"Glitch collage slice {i + 1}",
                4 + i,
                fullLength,
                new RectShape
                {
                    Name = $"Seeded glitch slice {i + 1}",
                    Width = { CurrentValue = width },
                    Height = { CurrentValue = height },
                    Fill = { CurrentValue = CreateLinearGradient(i % 2 == 0 ? palette.Accent : "#eeffffff", i % 2 == 0 ? palette.SecondaryAccent : palette.Accent) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y),
                                new RotationTransform(context.Random.Range(-7, 7))
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateMosaic(5 + (i % 4 * 3)),
                                CreateColorShift((int)(3 + (i % 5) * intensity)),
                                CreateDropShadow(16, 16, 8 * intensity, "#88000000")
                            }
                        }
                    }
                });

            JsonObject sliceJson = SerializeElement(slice);
            JsonObject sliceObject = GetFirstObjectJson(sliceJson);
            AddFloatAnimation(sliceObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.2 + (i * 0.06), 88, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 52 + (i % 4 * 8), typeof(SineEaseInOut)));
            AddFloatAnimation(GetTransformChildJson(sliceObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, x + context.Random.Range(-420, 420), typeof(CubicEaseOut)), (0.55 + (i * 0.05), x, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, endX, typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(sliceJson));
        }

        Element titleElement = CreateElement(
            "Glitch title",
            30,
            fullLength,
            new TextBlock
            {
                Name = "Hard cut title",
                Text = { CurrentValue = title },
                Size = { CurrentValue = 112 + context.Random.Range(-12, 10) },
                Spacing = { CurrentValue = 2 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse(palette.Foreground)) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-610 + context.Random.Range(-80, 120), 118 + context.Random.Range(-40, 60))
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateColorShift((int)(8 * intensity)),
                            CreateDropShadow(12, 18, 10, "#aa000000")
                        }
                    }
                }
            });
        JsonObject titleJson = SerializeElement(titleElement);
        JsonObject titleObject = GetFirstObjectJson(titleJson);
        AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.45, 100, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds - 0.25, 100, typeof(SineEaseInOut)), (context.Metadata.DurationSeconds, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 18, typeof(CubicEaseOut)), (0.9, 2, typeof(CubicEaseOut)), (context.Metadata.DurationSeconds, 8, typeof(SineEaseInOut)));
        elements.Add(DeserializeElement(titleJson));
        elements.Add(CreateTextElement("Glitch subtitle", "Hard cut subtitle", subtitle, 31, 30, 6, -600, 238, palette.Foreground, fullLength));

        return CreateRender(context, elements);
    }

    private static void AddNoiseDots(List<Element> elements, CompositionContext context, Palette palette, TimeSpan length, int zStart, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float x = (float)(context.Random.Noise("noise-x", i) * 820);
            float y = (float)(context.Random.Noise("noise-y", i) * 420);
            float size = 10 + (float)((context.Random.Noise("noise-size", i) + 1) * 10);
            Element dot = CreateElement(
                $"Seeded noise dot {i + 1}",
                zStart + i,
                length,
                new EllipseShape
                {
                    Name = $"Deterministic noise dot {i + 1}",
                    Width = { CurrentValue = size },
                    Height = { CurrentValue = size },
                    Fill = { CurrentValue = new SolidColorBrush(Color.Parse(i % 2 == 0 ? palette.Accent : palette.SecondaryAccent)) },
                    Transform =
                    {
                        CurrentValue = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(x, y)
                            }
                        }
                    },
                    FilterEffect =
                    {
                        CurrentValue = new FilterEffectGroup
                        {
                            Children =
                            {
                                CreateBlur(1.4f),
                                CreateDropShadow(0, 0, 10, "#8836f0ff")
                            }
                        }
                    }
                });

            JsonObject dotJson = SerializeElement(dot);
            JsonObject dotObject = GetFirstObjectJson(dotJson);
            AddFloatAnimation(dotObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.8 + (i * 0.05), 78, typeof(CubicEaseOut)), (length.TotalSeconds, 0, typeof(SineEaseInOut)));
            elements.Add(DeserializeElement(dotJson));
        }
    }

    private static CompositionRender CreateRender(CompositionContext context, IReadOnlyList<Element> elements)
    {
        return new CompositionRender(
            context.Spec.Name,
            context.Seed,
            CloneObject(context.InputProps),
            CloneObject(context.ResolvedProps),
            context.Metadata,
            context.Sequences.ToArray(),
            context.Transitions.ToArray(),
            new JsonObject
            {
                ["Duration"] = context.Metadata.Duration,
                ["Elements"] = new JsonArray(elements
                    .Select(SerializeElement)
                    .ToArray<JsonNode?>())
            });
    }

    private static Palette ResolvePalette(CompositionContext context, int offset)
    {
        Palette selected = s_palettes[Math.Abs((int)((StableHash(context.Seed) + (uint)offset) % (uint)s_palettes.Length))];
        SetIfMissing(context, "backgroundA", selected.BackgroundA);
        SetIfMissing(context, "backgroundB", selected.BackgroundB);
        SetIfMissing(context, "accent", selected.Accent);
        SetIfMissing(context, "secondaryAccent", selected.SecondaryAccent);
        SetIfMissing(context, "foreground", selected.Foreground);

        return new Palette(
            ReadString(context.ResolvedProps, "backgroundA", selected.BackgroundA),
            ReadString(context.ResolvedProps, "backgroundB", selected.BackgroundB),
            ReadString(context.ResolvedProps, "accent", selected.Accent),
            ReadString(context.ResolvedProps, "secondaryAccent", selected.SecondaryAccent),
            ReadString(context.ResolvedProps, "foreground", selected.Foreground));
    }

    private static void SetIfMissing(CompositionContext context, string name, string value)
    {
        if (!context.InputProps.ContainsKey(name))
        {
            context.ResolvedProps[name] = value;
        }
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

    private static T[] Deprioritize<T>(IReadOnlyList<T> source, IReadOnlyList<string>? names)
        where T : class
    {
        if (names is null || names.Count == 0)
        {
            return source.ToArray();
        }

        var recent = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return source
            .OrderBy(item => recent.Contains(GetTemplateName(item)) ? 1 : 0)
            .ToArray();
    }

    private static bool IsDeprioritized(string name, IReadOnlyList<string>? names)
    {
        return names?.Contains(name, StringComparer.OrdinalIgnoreCase) == true;
    }

    private static string GetTemplateName<T>(T item)
    {
        return item switch
        {
            CompositionTemplateSpec spec => spec.Name,
            CompositionTemplateSummary summary => summary.Name,
            _ => string.Empty
        };
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

    private static string[] SearchTokens(string query)
    {
        return query
            .Split([' ', '-', '_', '/', ',', ';', ':', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Element CreateElement(string name, int zIndex, TimeSpan length, EngineObject obj)
    {
        var element = new Element
        {
            Name = name,
            Start = TimeSpan.Zero,
            Length = length,
            ZIndex = zIndex
        };
        element.AddObject(obj);
        return element;
    }

    private static Element CreateTextElement(
        string elementName,
        string objectName,
        string text,
        int zIndex,
        float size,
        float spacing,
        float x,
        float y,
        string color,
        TimeSpan length)
    {
        Element element = CreateElement(
            elementName,
            zIndex,
            length,
            new TextBlock
            {
                Name = objectName,
                Text = { CurrentValue = text },
                Size = { CurrentValue = size },
                Spacing = { CurrentValue = spacing },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse(color)) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(x, y)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(8, 10, 10, "#88000000")
                        }
                    }
                }
            });

        JsonObject elementJson = SerializeElement(element);
        JsonObject textObject = GetFirstObjectJson(elementJson);
        AddFloatAnimation(textObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.9, 100, typeof(CubicEaseOut)), (length.TotalSeconds - 0.3, 100, typeof(SineEaseInOut)), (length.TotalSeconds, 0, typeof(SineEaseInOut)));
        return DeserializeElement(elementJson);
    }

    private static LinearGradientBrush CreateLinearGradient(string startColor, string endColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = { CurrentValue = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { CurrentValue = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
            GradientStops =
            {
                new GradientStop(Color.Parse(startColor), 0),
                new GradientStop(Color.Parse(endColor), 1)
            }
        };
    }

    private static RadialGradientBrush CreateRadialGradient(string innerColor, string outerColor)
    {
        return new RadialGradientBrush
        {
            Center = { CurrentValue = RelativePoint.Center },
            GradientOrigin = { CurrentValue = RelativePoint.Center },
            Radius = { CurrentValue = 72 },
            GradientStops =
            {
                new GradientStop(Color.Parse(innerColor), 0),
                new GradientStop(Color.Parse(outerColor), 1)
            }
        };
    }

    private static Pen CreatePen(string color, float thickness)
    {
        return new Pen
        {
            Brush = { CurrentValue = new SolidColorBrush(Color.Parse(color)) },
            Thickness = { CurrentValue = thickness }
        };
    }

    private static Blur CreateBlur(float sigma)
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(sigma, sigma);
        return blur;
    }

    private static Brightness CreateBrightness(float amount)
    {
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = amount;
        return brightness;
    }

    private static DropShadow CreateDropShadow(float x, float y, float sigma, string color)
    {
        var dropShadow = new DropShadow();
        dropShadow.Position.CurrentValue = new Point(x, y);
        dropShadow.Sigma.CurrentValue = new Size(sigma, sigma);
        dropShadow.Color.CurrentValue = Color.Parse(color);
        return dropShadow;
    }

    private static Saturate CreateSaturate(float amount)
    {
        var saturate = new Saturate();
        saturate.Amount.CurrentValue = amount;
        return saturate;
    }

    private static HueRotate CreateHueRotate(float angle)
    {
        var hueRotate = new HueRotate();
        hueRotate.Angle.CurrentValue = angle;
        return hueRotate;
    }

    private static HighContrast CreateHighContrast(float contrast)
    {
        var highContrast = new HighContrast();
        highContrast.Contrast.CurrentValue = contrast;
        return highContrast;
    }

    private static MosaicEffect CreateMosaic(float tileSize)
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(tileSize, tileSize);
        return mosaic;
    }

    private static ColorShift CreateColorShift(int offset)
    {
        var colorShift = new ColorShift();
        colorShift.RedOffset.CurrentValue = new PixelPoint(offset, 0);
        colorShift.GreenOffset.CurrentValue = PixelPoint.Origin;
        colorShift.BlueOffset.CurrentValue = new PixelPoint(-offset, 0);
        colorShift.AlphaOffset.CurrentValue = PixelPoint.Origin;
        return colorShift;
    }

    private static JsonObject SerializeElement(Element element)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        RemoveIds(json);
        return json;
    }

    private static Element DeserializeElement(JsonObject json)
    {
        return (Element)CoreSerializer.DeserializeFromJsonObject(CloneObject(json), typeof(Element))!;
    }

    private static JsonObject GetFirstObjectJson(JsonObject element)
    {
        return (JsonObject)((JsonArray)element[nameof(Element.Objects)]!)[0]!;
    }

    private static JsonObject GetTransformChildJson(JsonObject drawable, Type transformType)
    {
        string discriminator = IdentityHelper.WriteDiscriminator(transformType);
        JsonArray children = (JsonArray)drawable[nameof(Drawable.Transform)]![nameof(TransformGroup.Children)]!;
        return children
            .OfType<JsonObject>()
            .Single(child => string.Equals(child["$type"]?.GetValue<string>(), discriminator, StringComparison.Ordinal));
    }

    private static void AddFloatAnimation(JsonObject target, string property, params (double Seconds, float Value, Type Easing)[] keyframes)
    {
        JsonObject animations = target["Animations"] as JsonObject ?? [];
        animations[property] = CreateFloatAnimation(keyframes);
        target["Animations"] = animations;
    }

    private static JsonObject CreateFloatAnimation(params (double Seconds, float Value, Type Easing)[] keyframes)
    {
        string animationType = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<float>));
        string keyFrameType = IdentityHelper.WriteDiscriminator(typeof(KeyFrame<float>));
        return new JsonObject
        {
            ["$type"] = animationType,
            [nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(keyframes
                .Select(keyframe => new JsonObject
                {
                    ["$type"] = keyFrameType,
                    [nameof(KeyFrame.KeyTime)] = TimeSpan.FromSeconds(Math.Max(0, keyframe.Seconds)).ToString("c"),
                    [nameof(KeyFrame<float>.Value)] = keyframe.Value,
                    [nameof(KeyFrame.Easing)] = IdentityHelper.WriteDiscriminator(keyframe.Easing)
                })
                .ToArray<JsonNode?>())
        };
    }

    private static void RemoveIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(nameof(CoreObject.Id));
            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                RemoveIds(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                RemoveIds(child);
            }
        }
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

    private sealed record Palette(
        string BackgroundA,
        string BackgroundB,
        string Accent,
        string SecondaryAccent,
        string Foreground);

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
