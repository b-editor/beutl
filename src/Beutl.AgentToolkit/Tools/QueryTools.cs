using System.Collections;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ReadDocumentResponse(JsonObject Document, string SchemaVersion);

public sealed record GetExamplesResponse(
    string SchemaVersion,
    IReadOnlyList<DeclarativeExample> Examples,
    string SelectionHint);

public sealed record ListExamplesResponse(
    string SchemaVersion,
    IReadOnlyList<DeclarativeExampleSummary> Examples,
    string SelectionHint);

public sealed record ListCompositionsResponse(
    string SchemaVersion,
    string Seed,
    IReadOnlyList<CompositionTemplateSummary> Compositions,
    IReadOnlyList<string> RecentlyUsedCompositions,
    IReadOnlyList<string> PreAttachPreviewedCompositions,
    bool PreviewOnly,
    string SelectionHint);

public sealed record ListEffectsResponse(
    string SchemaVersion,
    IReadOnlyList<EffectSummary> Effects,
    string SelectionHint);

public sealed record ListEffectRecipesResponse(
    string SchemaVersion,
    IReadOnlyList<EffectRecipeSummary> Recipes,
    string SelectionHint);

public sealed record GetEffectRecipeResponse(
    string SchemaVersion,
    EffectRecipe Recipe,
    string UsageHint);

public sealed record GetCompositionResponse(
    string SchemaVersion,
    CompositionTemplateDetail Composition);

public sealed record RenderCompositionPatchResponse(
    string SchemaVersion,
    CompositionRender Composition,
    string UsageHint);

public sealed record GettingStartedResponse(
    string SchemaVersion,
    IReadOnlyList<string> RecommendedCalls,
    IReadOnlyDictionary<string, string> CategoryAliases,
    string RawHttpNote);

public sealed record CreativeDirectionResponse(
    string SchemaVersion,
    IReadOnlyList<string> DirectionAxes,
    IReadOnlyList<CreativeConceptPlan> ConceptPlans,
    IReadOnlyList<string> OverusedMotifs,
    IReadOnlyList<string> WorkflowHints,
    string SelectionHint);

public sealed record CreativeConceptPlan(
    string Concept,
    IReadOnlyList<string> Elements,
    IReadOnlyList<CreativeElementPlan> ElementPlan,
    IReadOnlyList<string> AnimatedProperties,
    IReadOnlyList<string> TimingPhases,
    IReadOnlyList<string> Effects,
    string PatchHint);

public sealed record CreativeElementPlan(
    string ElementName,
    string Role,
    string SuggestedObject,
    string Timing,
    string Motion);

public sealed record DocumentSummaryResponse(
    string Session,
    string Source,
    string RootId,
    string Name,
    int Width,
    int Height,
    string Duration,
    int ElementCount,
    IReadOnlyList<ElementSummary> Elements);

public sealed record ElementSummary(
    string Id,
    string Name,
    string Start,
    string Length,
    int ZIndex,
    IReadOnlyList<ObjectSummary> Objects);

public sealed record ObjectSummary(
    string Id,
    string Name,
    string Type,
    string Discriminator,
    IReadOnlyList<string> AnimatedProperties,
    IReadOnlyList<string> ExpressionProperties,
    IReadOnlyList<string> BrushProperties,
    IReadOnlyList<string> EffectProperties,
    IReadOnlyList<string> NestedAnimatedProperties);

[McpServerToolType]
public sealed class QueryTools(AgentSessionManager sessions) : ToolBase
{
    private readonly SchemaGenerator _schemaGenerator = new();
    private readonly CompositionTemplateCatalog _compositionCatalog = new();

    [McpServerTool(Name = "get_started")]
    [Description("Returns a compact, low-context guide for using the Beutl Agent Editing Toolkit. Use this first when an agent only has the MCP endpoint URL.")]
    public ToolResult<GettingStartedResponse> GetStarted()
    {
        return Execute(() => new GettingStartedResponse(
            SchemaVersion.Current,
            [
                "Call attach_active_editor for an open editor scene; if it fails or no editor is available, call create_project or open_project for a file-backed session instead of writing a one-off generator.",
                "Call read_document_summary to inspect progress without the full document.",
                "For original creative briefs, call list_creative_directions, pick a conceptPlan, read_document, and get_schema only for the drawable/effect types you need, then author a custom declarative patch instead of cloning a starter.",
                "Call list_effects and list_effect_recipes to discover Beutl's visual effect palette before choosing a repeated look; for organic heat/ink/glass/noise fields, consider an SKSLScriptEffect shader recipe instead of stacking only blurred gradients.",
                "For no-context motion graphics, avoid overused orbit/radar/map/signal/dashboard motifs unless the user asks for them.",
                "For visible progress, apply large scenes in stages: background first, then motion elements, then text/effects; for conceptPlans, use exactly one elementPlan item per plan/apply/save stage unless the user explicitly asks for a combined edit.",
                "For unconstrained creative briefs, keep project/video/still basenames neutral and record the chosen concept name in notes instead of filenames.",
                "New timeline Elements need '$type': '[Beutl.ProjectSystem]:Element'. Existing Elements keep Id; genuinely new Elements and Objects omit Id. If you need structure only, fetch the targeted insert-new-element-skeleton example instead of a full-scene starter.",
                "Animation KeyFrame times are scene timeline times in serialized toolkit patches. For Elements with nonzero Start, set keyframes to scene times that intersect the frames you will render.",
                "Call plan_edit with the custom patch and schemaVersion=1.",
                "Call apply_edit with plan_edit.planId when present, especially for large edits. Otherwise pass plan_edit.expectedChangeSet exactly as returned; do not summarize or rewrite the array.",
                "For file-backed sessions, call save_project after each major successful apply_edit so partial progress is durable.",
                "For selected creative conceptPlans, verify read_document_summary contains the planned element names before rendering or exporting.",
                "Use list_compositions, get_composition, and plan_composition only when the user explicitly asks for a reusable template, starter, or named composition style.",
                "When using a composition template, call list_compositions, choose a specific returned name that matches the user's request, then pass that name to plan_composition and apply_composition with the returned planId.",
                "Use render_composition_patch only when the client explicitly needs the generated template patch JSON.",
                "Call list_examples/get_examples for small schema snippets or as a fallback when a user asks for an example; full-scene starters are hidden by default.",
                "Call render_still for representative frames, record planned-element visibility/readability plus layer density/contrast, then evaluate_motion_variation to check temporal change before export_video."
            ],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["visualEffect"] = "FilterEffect",
                ["effect"] = "FilterEffect",
                ["filter"] = "FilterEffect",
                ["videoEffect"] = "FilterEffect",
                ["text"] = "TextBlock",
                ["typography"] = "TextBlock",
                ["label"] = "TextBlock",
                ["fill"] = "Brush",
                ["gradient"] = "Brush",
                ["stroke"] = "Pen",
                ["ease"] = "Easing"
            },
            "Raw HTTP MCP responses are Server-Sent Events. Read the JSON from the data: line, then decode result.content[0].text as the tool JSON payload. notifications/initialized may return no body."));
    }

    [McpServerTool(Name = "list_creative_directions")]
    [Description("Returns non-template creative direction axes for original motion graphics. Use before authoring when the brief is vague or absent.")]
    public ToolResult<CreativeDirectionResponse> ListCreativeDirections(string? brief = null)
    {
        return Execute(() =>
        {
            IReadOnlyList<CreativeConceptPlan> conceptPlans = OrderCreativeConceptPlans(
                CreateConceptPlans(),
                brief,
                sessions.NextCreativeDirectionRequestIndex());
            string selectionHint = string.IsNullOrWhiteSpace(brief)
                ? "No brief was supplied. Compare at least two conceptPlans, avoid any concept close to your last output, then implement the chosen element plan with a custom patch. Returned order is seeded and is not a ranking."
                : $"Compare at least two conceptPlans before choosing, avoid any concept close to your last output, and reinterpret the brief without copying starter scenes: {brief.Trim()}";

            return new CreativeDirectionResponse(
                SchemaVersion.Current,
                [
                    "material: paper cutouts, ink bleed, glass refraction, fabric folds, projected light, ceramic glaze, thermal camera, risograph print, CRT phosphor, blueprint pencil",
                    "motion: bloom, peel, fold, pour, shatter, magnetic attraction, wave interference, hand-drawn reveal, parallax drift, stop-motion stepping",
                    "composition: macro close-up, off-axis crop, split depth planes, negative-space title, diagonal editorial grid, frame-within-frame, vertical poster stack",
                    "typography: small caption system, kinetic word fragments, numeric countdown, subtitle-only rhythm, oversized cropped letterforms",
                    "palette: warm/cool clash, muted paper plus neon accent, monochrome with one warning color, daylight pastels, high-contrast print inks",
                    "detail density: crop marks, micro-captions, texture grains, edge highlights, calibration ticks, fracture lines, shimmer bands",
                    "procedural surface: use SKSLScriptEffect for organic heat, ink, glass, smoke, grain, or caustic fields when gradients alone look flat"
                ],
                conceptPlans,
                [
                    "orbit rings",
                    "radar sweeps",
                    "map or atlas labels",
                    "signal nodes",
                    "dashboard metric bars",
                    "dark teal background with cyan/magenta neon"
                ],
                [
                    "Do not default to the first returned concept. Compare at least two conceptPlans, reject concepts close to your last output, then choose the strongest element plan.",
                    "Map the chosen conceptPlan's elements to named Element/Object entries before writing a patch.",
                    "Plan/apply/save in small stages that follow the chosen elementPlan. Use exactly one elementPlan item per stage; avoid one huge full-scene patch because large expectedChangeSet payloads may be omitted in favor of planId.",
                    "After read_document_summary, compare actual element names with the chosen elementPlan and revise missing planned parts before rendering.",
                    "For unconstrained briefs, keep project/video/still basenames neutral instead of naming files after the chosen concept.",
                    "After rendering stills, record which planned elements are visible/readable in each still, plus whether each development/resolution frame has at least three visible layer types and readable text contrast; revise if it does not.",
                    "Use at least three timing phases and animate multiple property families, not only X position and opacity.",
                    "For organic abstract concepts, consider SKSLScriptEffect from list_effect_recipes(intent: 'shader organic') and verify the shader with render_still before export.",
                    "Use scene-time KeyFrame values that intersect sampled still/video frames, especially for Elements with nonzero Start offsets.",
                    "Name the concept in any notes or output summary before creating elements.",
                    "Use list_effects/list_effect_recipes for available effects, then build the scene with plan_edit/apply_edit.",
                    "Keep full-scene examples and composition templates for explicit template/starter requests only.",
                    "Verify at least three stills, run evaluate_motion_variation, and export a short video preview when the encoder is available."
                ],
                selectionHint);
        });
    }

    [McpServerTool(Name = "get_schema")]
    [Description("Returns the capability schema for registered editable types, optionally filtered by type or category. Category aliases such as visualEffect, effect, filter, videoEffect, text, typography, label, fill, stroke, and ease are accepted. Examples are opt-in so creative briefs do not anchor on starter scenes; set includeExamples=true when snippets are explicitly needed.")]
    public ToolResult<CapabilitySchema> GetSchema(
        string? type = null,
        string? category = null,
        bool includeProperties = true,
        bool includeExamples = false)
    {
        return Execute(() =>
        {
            CapabilitySchema schema = _schemaGenerator.Generate(type, category, includeProperties, includeExamples);
            if ((type is not null || category is not null) && schema.Types.Count == 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.UnknownType,
                    $"No schema entries matched type='{type}' category='{category}'.",
                    type ?? category));
            }

            return schema;
        });
    }

    [McpServerTool(Name = "list_examples")]
    [Description("Returns compact example names, descriptions, categories, and tags without large patches. Full-scene starters are hidden by default; use includeStarters=true only for explicit starter/template requests.")]
    public ToolResult<ListExamplesResponse> ListExamples(string? type = null, string? category = null, bool includeStarters = false)
    {
        return Execute(() =>
        {
            IReadOnlyList<DeclarativeExampleSummary> examples = FilterExamples(
                _schemaGenerator.ListExamples(type, category),
                includeStarters);
            return new ListExamplesResponse(
                SchemaVersion.Current,
                OrderExamples(Shuffle(examples)),
                includeStarters
                    ? "Starter examples are included because includeStarters=true. Use get_examples with a specific name and adapt the structure to the brief."
                    : "Full-scene starters are hidden by default. Use examples as small snippets; for original briefs, call list_creative_directions and author a custom patch with plan_edit/apply_edit.");
        });
    }

    [McpServerTool(Name = "get_examples")]
    [Description("Returns reusable declarative patch examples without the full property schema. Prefer list_examples first, then pass name to fetch exactly one patch. Full-scene starters are hidden by default unless name is provided or includeStarters=true.")]
    public ToolResult<GetExamplesResponse> GetExamples(string? type = null, string? category = null, string? name = null, bool includeStarters = false)
    {
        return Execute(() => new GetExamplesResponse(
            SchemaVersion.Current,
            string.IsNullOrWhiteSpace(name)
                ? OrderExamples(Shuffle(FilterExamples(_schemaGenerator.GenerateExamples(type, category, name), includeStarters)))
                : _schemaGenerator.GenerateExamples(type, category, name),
            string.IsNullOrWhiteSpace(name) && !includeStarters
                ? "Full-scene starters are hidden by default. Pass name for an explicit starter, or includeStarters=true when the user asks for starters."
                : "If more than one example is returned, the order is shuffled and recently used composition styles are moved to the end. Use name to fetch a single snippet or explicit starter; for original briefs, build a custom patch instead of cloning an empty-scene example."));
    }

    [McpServerTool(Name = "list_effects")]
    [Description("Returns Beutl FilterEffect types with intent tags, property names, notes, and GPU requirements. Use before motion-graphics authoring to avoid repeating the same blur/shadow look.")]
    public ToolResult<ListEffectsResponse> ListEffects(string? intent = null, bool includePropertyNames = true)
    {
        return Execute(() => new ListEffectsResponse(
            SchemaVersion.Current,
            _schemaGenerator.ListEffects(intent, includePropertyNames),
            "Filter by intent such as glow, color, grade, glitch, outline, keying, motion, composite, gpu, or advanced. Call get_schema with type=<Name> for full property descriptors."));
    }

    [McpServerTool(Name = "list_effect_recipes")]
    [Description("Returns compact effect recipe names. Includes curated chains plus one single-effect recipe for every registered Beutl FilterEffect.")]
    public ToolResult<ListEffectRecipesResponse> ListEffectRecipes(string? intent = null)
    {
        return Execute(() => new ListEffectRecipesResponse(
            SchemaVersion.Current,
            _schemaGenerator.ListEffectRecipes(intent),
            "Call get_effect_recipe with a recipe name, then apply the returned patch through plan_edit/apply_edit after replacing placeholder element/drawable Ids."));
    }

    [McpServerTool(Name = "get_effect_recipe")]
    [Description("Returns a declarative patch recipe for a visual effect chain or a single Beutl FilterEffect. Pass name from list_effect_recipes or an intent tag.")]
    public ToolResult<GetEffectRecipeResponse> GetEffectRecipe(string? name = null, string? intent = null)
    {
        return Execute(() => new GetEffectRecipeResponse(
            SchemaVersion.Current,
            _schemaGenerator.GetEffectRecipe(name, intent),
            "Replace <element-id> and <drawable-id> with Ids from read_document/read_document_summary, then pass recipe.patch to plan_edit/apply_edit with schemaVersion=1."));
    }

    [McpServerTool(Name = "list_compositions")]
    [Description("Returns optional reusable composition templates for explicit template/starter requests. Templates include props, style axes, default metadata, and deterministic shuffled order. If seed is omitted, the active session default seed is used. Recently applied templates are moved to the end by default.")]
    public ToolResult<ListCompositionsResponse> ListCompositions(string? tag = null, string? seed = null, bool avoidRecent = true)
    {
        return Execute(() =>
        {
            bool previewOnly = !sessions.HasActiveSession && string.IsNullOrWhiteSpace(seed);
            IReadOnlyList<string> recent = sessions.GetRecentCompositions();
            IReadOnlyList<string> avoided = avoidRecent ? sessions.GetAvoidedCompositions() : [];
            CompositionTemplateList list = _compositionCatalog.List(tag, sessions.ResolveCompositionSeed(seed), avoided);
            if (previewOnly && avoidRecent && list.Compositions.FirstOrDefault() is { } first)
            {
                sessions.RecordPreAttachCompositionPreview(first.Name);
            }

            return new ListCompositionsResponse(
                SchemaVersion.Current,
                list.Seed,
                list.Compositions,
                recent,
                sessions.GetPreAttachPreviewedCompositions(),
                previewOnly,
                previewOnly
                    ? "This list is a pre-attach preview. Open/create a Beutl project, call attach_active_editor, then call list_compositions again before choosing a named template. The first previewed composition is deprioritized after attach."
                    : "Compositions are shuffled by seed, then recently applied or pre-attach previewed names are moved to the end when avoidRecent=true. Use a specific returned name only when the user explicitly asked for a template/starter; original creative briefs should use custom plan_edit/apply_edit patches. Explicitly selecting an avoided name is rejected unless avoidRecent=false.");
        });
    }

    [McpServerTool(Name = "get_composition")]
    [Description("Returns one reusable composition template contract: defaultProps, prop descriptors, calculated default metadata, Sequence-like timing, transitions, tags, and style axes.")]
    public ToolResult<GetCompositionResponse> GetComposition(string name)
    {
        return Execute(() => new GetCompositionResponse(
            SchemaVersion.Current,
            _compositionCatalog.Get(name)));
    }

    [McpServerTool(Name = "render_composition_patch")]
    [Description("Materializes an explicitly named reusable composition template into a declarative Beutl JSON Merge Patch. Use only when the user asked for a template/starter or a named composition style.")]
    public ToolResult<RenderCompositionPatchResponse> RenderCompositionPatch(
        string? name = null,
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null,
        bool avoidRecent = true)
    {
        return Execute(() =>
        {
            RequireCompositionName(name);
            return new RenderCompositionPatchResponse(
                SchemaVersion.Current,
                _compositionCatalog.Render(
                    name,
                    tag,
                    inputProps,
                    sessions.ResolveCompositionSeed(seed),
                    avoidRecent ? sessions.GetAvoidedCompositions() : null,
                    EnforceFirstSelection(name, avoidRecent)),
                "Pass composition.patch to plan_edit/apply_edit with schemaVersion=1. Use the returned seed to reproduce or intentionally vary this named template.");
        });
    }

    private static bool EnforceFirstSelection(string? name, bool avoidRecent)
    {
        return avoidRecent && !string.IsNullOrWhiteSpace(name);
    }

    private static void RequireCompositionName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "Composition templates require an explicit template name.",
            null,
            "Call list_compositions to inspect options, then pass a returned name only when the user explicitly asked for a reusable template/starter. For original creative briefs, author a custom patch with plan_edit/apply_edit."));
    }

    private static T[] Shuffle<T>(IReadOnlyList<T> source)
    {
        T[] items = source.ToArray();
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }

    private T[] OrderExamples<T>(IReadOnlyList<T> source)
    {
        IReadOnlyList<string> recent = sessions.GetRecentCompositions();
        if (recent.Count == 0)
        {
            return source.ToArray();
        }

        var recentSet = new HashSet<string>(recent, StringComparer.OrdinalIgnoreCase);
        return source
            .OrderBy(item =>
            {
                string? compositionName = item switch
                {
                    DeclarativeExampleSummary summary => CompositionTemplateCatalog.TryInferTemplateNameFromExampleName(summary.Name),
                    DeclarativeExample example => CompositionTemplateCatalog.TryInferTemplateNameFromExampleName(example.Name)
                                                  ?? CompositionTemplateCatalog.TryInferTemplateName(example.Patch),
                    _ => null
                };

                return compositionName is not null && recentSet.Contains(compositionName) ? 1 : 0;
            })
            .ToArray();
    }

    private static IReadOnlyList<DeclarativeExampleSummary> FilterExamples(
        IReadOnlyList<DeclarativeExampleSummary> examples,
        bool includeStarters)
    {
        return includeStarters
            ? examples
            : examples.Where(example => !IsStarterExample(example.Name, example.Tags)).ToArray();
    }

    private static IReadOnlyList<DeclarativeExample> FilterExamples(
        IReadOnlyList<DeclarativeExample> examples,
        bool includeStarters)
    {
        return includeStarters
            ? examples
            : examples.Where(example => !IsStarterExample(example.Name, [])).ToArray();
    }

    private static bool IsStarterExample(string name, IReadOnlyList<string> tags)
    {
        return name.StartsWith("create-empty-scene-", StringComparison.OrdinalIgnoreCase)
               || tags.Any(tag => string.Equals(tag, "starter", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(tag, "empty-scene", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<CreativeConceptPlan> OrderCreativeConceptPlans(
        IReadOnlyList<CreativeConceptPlan> plans,
        string? brief,
        int requestIndex)
    {
        if (plans.Count <= 1)
        {
            return plans.ToArray();
        }

        int baseOffset = ComputeCreativeConceptBaseOffset(brief, plans.Count);
        int offset = ((baseOffset - 1 + requestIndex) % (plans.Count - 1)) + 1;
        var ordered = new CreativeConceptPlan[plans.Count];
        for (int i = 0; i < ordered.Length; i++)
        {
            ordered[i] = plans[(i + offset) % plans.Count];
        }

        return ordered;
    }

    private static int ComputeCreativeConceptBaseOffset(string? brief, int count)
    {
        string seedText = string.IsNullOrWhiteSpace(brief)
            ? (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60).ToString()
            : brief.Trim();

        unchecked
        {
            uint hash = 2166136261;
            foreach (char ch in seedText)
            {
                hash ^= char.ToUpperInvariant(ch);
                hash *= 16777619;
            }

            return (int)(hash % (uint)(count - 1)) + 1;
        }
    }

    [McpServerTool(Name = "read_document_summary")]
    [Description("Reads a compact summary of the current scene without returning the full declarative JSON. Use this for live progress observation or before deciding whether a full read_document call is necessary.")]
    public ToolResult<DocumentSummaryResponse> ReadDocumentSummary()
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (session.Root is not Scene scene)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Current root '{session.Root.GetType().FullName}' is not a Scene.",
                    session.Root.Id.ToString()));
            }

            return new DocumentSummaryResponse(
                session.SessionId,
                session.Source.ToString(),
                scene.Id.ToString(),
                scene.Name,
                scene.FrameSize.Width,
                scene.FrameSize.Height,
                scene.Duration.ToString("c"),
                scene.Children.Count,
                scene.Children.Select(CreateElementSummary).ToArray());
        });
    }

    [McpServerTool(Name = "read_document")]
    [Description("Reads the current declarative document, or a subtree selected by rootId. This can be large; use read_document_summary for progress checks. In the in-app host, call attach_active_editor first; in the stdio host, call open_project or create_project first.")]
    public ToolResult<ReadDocumentResponse> ReadDocument(string? rootId = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            JsonObject document;
            if (string.IsNullOrWhiteSpace(rootId))
            {
                document = session.Documents.Read(session.Root);
            }
            else if (Guid.TryParse(rootId, out Guid id)
                     && IdentityHelper.FindById(session.Root, id) is CoreObject subtree)
            {
                document = CoreSerializer.SerializeToJsonObject(
                    subtree,
                    new CoreSerializerOptions
                    {
                        BaseUri = subtree.Uri,
                        Mode = CoreSerializationMode.EmbedReferencedObjects
                    });
                SchemaVersion.Stamp(document);
            }
            else
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.StaleHandle,
                    $"No entity with Id '{rootId}' exists in the current session.",
                    rootId));
            }

            return new ReadDocumentResponse(document, SchemaVersion.Current);
        });
    }

    private static IReadOnlyList<CreativeConceptPlan> CreateConceptPlans()
    {
        return
        [
            new CreativeConceptPlan(
                "Projected ink fold",
                [
                    "warm paper background plate",
                    "large folding cutout plane",
                    "ink-gradient light ribbon",
                    "small calibration ticks",
                    "cropped kinetic title"
                ],
                [
                    new CreativeElementPlan(
                        "paper-backplate",
                        "Full-frame warm paper texture and subtle drift.",
                        "RectShape with gradient Fill",
                        "00:00-08:00",
                        "Slow TranslateTransform drift plus low-opacity brightness pulse."),
                    new CreativeElementPlan(
                        "folding-cutout",
                        "Large geometric plane that folds into the frame.",
                        "RectShape or PathGeometry-backed GeometryShape",
                        "00:00-03:00",
                        "Scale/Rotation reveal with DropShadow depth change."),
                    new CreativeElementPlan(
                        "ink-ribbon",
                        "Sweeping gradient ribbon that crosses the title.",
                        "RectShape with LinearGradientBrush",
                        "01:50-06:00",
                        "Translate across the frame while GradientStops.Offset/Color animate."),
                    new CreativeElementPlan(
                        "calibration-ticks",
                        "Small tick marks that add density without becoming a dashboard.",
                        "Thin RectShape objects",
                        "02:00-07:50",
                        "Staggered Opacity and short TranslateTransform offsets."),
                    new CreativeElementPlan(
                        "cropped-title",
                        "Kinetic cropped title that resolves at the end.",
                        "TextBlock",
                        "03:00-08:00",
                        "Opacity, Spacing, and slight Y motion settle into final lockup.")
                ],
                [
                    "Transform.Children.TranslateTransform.X/Y",
                    "Transform.Children.RotationTransform.Rotation",
                    "Opacity",
                    "Fill.GradientStops.Color/Offset",
                    "FilterEffect.Children.Blur.Sigma"
                ],
                [
                    "0-25%: quiet paper reveal and cutout fold-in",
                    "25-75%: ribbon sweep crosses title and shifts color",
                    "75-100%: title resolves while background drifts out"
                ],
                [
                    "Blur",
                    "DropShadow",
                    "Brightness"
                ],
                "Create at least five named Elements/Objects from this plan before calling plan_edit."),
            new CreativeConceptPlan(
                "Magnetic type shards",
                [
                    "soft neutral background",
                    "three to five letter-fragment TextBlock objects",
                    "thin attraction lines",
                    "accent particle dots",
                    "final grouped title lockup"
                ],
                [
                    new CreativeElementPlan(
                        "neutral-field",
                        "Quiet base layer that keeps contrast readable.",
                        "RectShape with SolidColorBrush or LinearGradientBrush",
                        "00:00-08:00",
                        "Subtle opacity and scale breathing."),
                    new CreativeElementPlan(
                        "type-shard-a",
                        "First fragment of the final word.",
                        "TextBlock",
                        "00:00-06:20",
                        "Independent X/Y/Rotation overshoot into lockup."),
                    new CreativeElementPlan(
                        "type-shard-b",
                        "Second fragment with offset timing.",
                        "TextBlock",
                        "00:20-06:20",
                        "Counter-motion plus Spacing compression."),
                    new CreativeElementPlan(
                        "attraction-lines",
                        "Thin connector strokes that imply pull without orbit motifs.",
                        "RectShape lines",
                        "01:00-05:40",
                        "ScaleX/Opacity stretch and fade in staggered bursts."),
                    new CreativeElementPlan(
                        "accent-dots",
                        "Small particles that fill negative space.",
                        "EllipseShape objects",
                        "01:20-07:00",
                        "Short TranslateTransform hops and opacity flickers.")
                ],
                [
                    "Transform.Children.TranslateTransform.X/Y",
                    "Transform.Children.RotationTransform.Rotation",
                    "Opacity",
                    "TextBlock.Spacing",
                    "BlendMode"
                ],
                [
                    "0-20%: fragments enter separately",
                    "20-70%: fragments overshoot and orbit-free attraction lines stretch",
                    "70-100%: fragments snap into readable lockup"
                ],
                [
                    "DropShadow",
                    "Brightness",
                    "FilterEffectGroup"
                ],
                "Map each shard to its own element or object so the motion can vary by phase."),
            new CreativeConceptPlan(
                "Thermal bloom diagram",
                [
                    "dark-to-warm thermal background",
                    "two translucent heat blobs",
                    "thin contour strokes",
                    "numeric micro-captions",
                    "white final label"
                ],
                [
                    new CreativeElementPlan(
                        "thermal-background",
                        "Full-frame dark-to-warm field.",
                        "RectShape with SKSLScriptEffect or LinearGradientBrush",
                        "00:00-08:00",
                        "Procedural shader time/progress field, or gradient offset and brightness drift."),
                    new CreativeElementPlan(
                        "heat-blob-left",
                        "Large translucent bloom from one side.",
                        "EllipseShape with Blur",
                        "00:00-05:50",
                        "Scale and blur sigma expand then cool."),
                    new CreativeElementPlan(
                        "heat-blob-right",
                        "Second bloom with delayed counter movement.",
                        "EllipseShape with Blur",
                        "00:40-06:50",
                        "Translate and opacity phase shifted from first blob."),
                    new CreativeElementPlan(
                        "contour-strokes",
                        "Thin contour marks over the blooms.",
                        "RectShape or GeometryShape strokes",
                        "02:00-07:00",
                        "Trim/Opacity staggered in waves."),
                    new CreativeElementPlan(
                        "micro-captions",
                        "Small numeric labels and final white title.",
                        "TextBlock",
                        "02:50-08:00",
                        "Caption opacity cascade, then final label settle.")
                ],
                [
                    "Transform.Children.ScaleTransform.Scale",
                    "Transform.Children.TranslateTransform.X/Y",
                    "Opacity",
                    "Fill.GradientStops.Offset",
                    "FilterEffect.Children.Blur.Sigma"
                ],
                [
                    "0-30%: heat blobs bloom from opposite corners",
                    "30-70%: contours and captions stagger in",
                    "70-100%: final label appears while blobs cool"
                ],
                [
                    "SKSLScriptEffect",
                    "Blur",
                    "Brightness",
                    "DropShadow"
                ],
                "Use separate background, blob, contour, caption, and label layers for visual density."),
            new CreativeConceptPlan(
                "Glass prism clock",
                [
                    "cool gray glass field",
                    "three refracted prism slabs",
                    "thin caustic light streaks",
                    "tiny timecode ticks",
                    "minimal title aperture"
                ],
                [
                    new CreativeElementPlan(
                        "glass-field",
                        "Neutral reflective field that anchors refraction.",
                        "RectShape with LinearGradientBrush",
                        "00:00-08:00",
                        "Gradient offset, opacity, and slight scale breathing."),
                    new CreativeElementPlan(
                        "prism-slabs",
                        "Three translucent slabs crossing at different depths.",
                        "RectShape objects with alpha Fill and DropShadow",
                        "00:20-06:50",
                        "Independent X/Y, Rotation, and blur changes."),
                    new CreativeElementPlan(
                        "caustic-streaks",
                        "Narrow light streaks that sweep across slab edges.",
                        "Thin RectShape objects",
                        "01:00-07:20",
                        "ScaleX, opacity, and gradient color pulse."),
                    new CreativeElementPlan(
                        "timecode-ticks",
                        "Small non-dashboard tick marks for temporal texture.",
                        "TextBlock or thin RectShape objects",
                        "02:00-07:50",
                        "Staggered opacity with short Y offsets."),
                    new CreativeElementPlan(
                        "aperture-title",
                        "Minimal title revealed through a moving crop-like opening.",
                        "TextBlock",
                        "03:20-08:00",
                        "Opacity, spacing, and subtle scale settle.")
                ],
                [
                    "Transform.Children.TranslateTransform.X/Y",
                    "Transform.Children.RotationTransform.Rotation",
                    "Transform.Children.ScaleTransform.Scale",
                    "Opacity",
                    "Fill.GradientStops.Color/Offset",
                    "FilterEffect.Children.Blur.Sigma"
                ],
                [
                    "0-25%: glass field and prism slabs slide into depth",
                    "25-70%: caustic streaks and ticks sweep across",
                    "70-100%: aperture title resolves as refraction calms"
                ],
                [
                    "Blur",
                    "DropShadow",
                    "Brightness"
                ],
                "Use translucent slab, streak, tick, and aperture layers so the piece is not just a glow pass."),
            new CreativeConceptPlan(
                "Risograph registration drift",
                [
                    "off-white print stock",
                    "misregistered color plates",
                    "halftone dot field",
                    "crop marks and fold guides",
                    "overprinted headline"
                ],
                [
                    new CreativeElementPlan(
                        "print-stock",
                        "Off-white base plate with subtle paper motion.",
                        "RectShape with SolidColorBrush or LinearGradientBrush",
                        "00:00-08:00",
                        "Opacity and slight Y drift."),
                    new CreativeElementPlan(
                        "color-plates",
                        "Two or three overlapping print-color blocks.",
                        "RectShape objects with translucent fills",
                        "00:00-06:40",
                        "Misregistered X/Y offsets converge and overshoot."),
                    new CreativeElementPlan(
                        "halftone-field",
                        "Small dot texture across part of the frame.",
                        "EllipseShape objects or repeated small RectShape dots",
                        "01:00-07:00",
                        "Staggered opacity, scale, and short drift."),
                    new CreativeElementPlan(
                        "crop-guides",
                        "Editorial crop marks and fold guide lines.",
                        "Thin RectShape objects",
                        "01:40-07:50",
                        "Line scale and opacity reveal in offset groups."),
                    new CreativeElementPlan(
                        "overprint-headline",
                        "Bold headline that briefly misregisters then locks.",
                        "TextBlock",
                        "02:50-08:00",
                        "Spacing, X/Y offset, and opacity snap into final print.")
                ],
                [
                    "Transform.Children.TranslateTransform.X/Y",
                    "Transform.Children.ScaleTransform.Scale",
                    "Opacity",
                    "TextBlock.Spacing",
                    "Fill.Color"
                ],
                [
                    "0-25%: print plates arrive with visible offset",
                    "25-75%: halftone and crop marks build density",
                    "75-100%: overprint headline locks into registration"
                ],
                [
                    "Brightness",
                    "DropShadow",
                    "FilterEffectGroup"
                ],
                "Keep the print-stock, plates, dots, guides, and headline as separate planned layers."),
            new CreativeConceptPlan(
                "Ceramic glaze fracture",
                [
                    "matte ceramic background",
                    "glaze puddle ellipses",
                    "fine crack lines",
                    "kiln heat shimmer bands",
                    "small maker mark title"
                ],
                [
                    new CreativeElementPlan(
                        "ceramic-ground",
                        "Matte base with a softly uneven color field.",
                        "RectShape with LinearGradientBrush",
                        "00:00-08:00",
                        "Gradient color drift and subtle opacity pulse."),
                    new CreativeElementPlan(
                        "glaze-puddles",
                        "Overlapping glossy puddles moving like liquid glaze.",
                        "EllipseShape objects with blur/brightness",
                        "00:40-06:50",
                        "Scale, X/Y drift, opacity, and blur sigma changes."),
                    new CreativeElementPlan(
                        "fracture-lines",
                        "Thin crack marks that branch across the frame.",
                        "RectShape or GeometryShape strokes",
                        "01:40-07:20",
                        "ScaleX/opacity reveal with staggered offsets."),
                    new CreativeElementPlan(
                        "heat-shimmer-bands",
                        "Soft horizontal bands suggesting kiln heat.",
                        "Translucent RectShape objects",
                        "02:00-07:50",
                        "Y drift, opacity wave, and brightness pulse."),
                    new CreativeElementPlan(
                        "maker-mark",
                        "Small final title mark rather than a large poster headline.",
                        "TextBlock",
                        "03:50-08:00",
                        "Opacity, spacing, and slight rotation settle.")
                ],
                [
                    "Transform.Children.TranslateTransform.X/Y",
                    "Transform.Children.ScaleTransform.Scale",
                    "Transform.Children.RotationTransform.Rotation",
                    "Opacity",
                    "FilterEffect.Children.Blur.Sigma",
                    "TextBlock.Spacing"
                ],
                [
                    "0-30%: ceramic ground warms and glaze puddles spread",
                    "30-75%: fracture lines and shimmer bands reveal",
                    "75-100%: maker mark resolves as glaze motion slows"
                ],
                [
                    "Blur",
                    "Brightness",
                    "DropShadow"
                ],
                "Use puddles, fracture marks, shimmer bands, and maker mark together so the ceramic premise is readable.")
        ];
    }

    private static ElementSummary CreateElementSummary(Element element)
    {
        return new ElementSummary(
            element.Id.ToString(),
            element.Name,
            element.Start.ToString("c"),
            element.Length.ToString("c"),
            element.ZIndex,
            element.Objects.Select(CreateObjectSummary).ToArray());
    }

    private static ObjectSummary CreateObjectSummary(EngineObject obj)
    {
        IProperty[] properties = obj.Properties.ToArray();
        return new ObjectSummary(
            obj.Id.ToString(),
            obj.Name,
            obj.GetType().FullName ?? obj.GetType().Name,
            IdentityHelper.WriteDiscriminator(obj.GetType()),
            properties.Where(property => property.Animation is not null).Select(property => property.Name).ToArray(),
            properties.Where(property => property.HasExpression).Select(property => property.Name).ToArray(),
            properties.Where(IsBrushProperty).Select(property => property.Name).ToArray(),
            properties.Where(IsEffectProperty).Select(property => property.Name).ToArray(),
            properties.SelectMany(property => CreateNestedAnimatedPropertySummaries(
                    property.Name,
                    property.CurrentValue,
                    new HashSet<Guid>()))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());
    }

    private static IEnumerable<string> CreateNestedAnimatedPropertySummaries(string path, object? value, ISet<Guid> visited)
    {
        switch (value)
        {
            case EngineObject engineObject:
                if (!visited.Add(engineObject.Id))
                {
                    yield break;
                }

                foreach (IProperty property in engineObject.Properties)
                {
                    string propertyPath = $"{path}.{property.Name}";
                    if (property.Animation is not null)
                    {
                        yield return propertyPath;
                    }

                    foreach (string child in CreateNestedAnimatedPropertySummaries(propertyPath, property.CurrentValue, visited))
                    {
                        yield return child;
                    }

                    if (property is IListProperty listProperty)
                    {
                        foreach (string child in CreateListNestedAnimatedPropertySummaries(propertyPath, listProperty, visited))
                        {
                            yield return child;
                        }
                    }
                }

                break;
            case IEnumerable enumerable when value is not string:
                int index = 0;
                foreach (object? item in enumerable)
                {
                    foreach (string child in CreateNestedAnimatedPropertySummaries($"{path}[{index}]", item, visited))
                    {
                        yield return child;
                    }

                    index++;
                }

                break;
        }
    }

    private static IEnumerable<string> CreateListNestedAnimatedPropertySummaries(string path, IListProperty listProperty, ISet<Guid> visited)
    {
        int index = 0;
        foreach (object? item in listProperty)
        {
            foreach (string child in CreateNestedAnimatedPropertySummaries($"{path}[{index}]", item, visited))
            {
                yield return child;
            }

            index++;
        }
    }

    private static bool IsBrushProperty(IProperty property)
    {
        return typeof(Brush).IsAssignableFrom(property.ValueType)
               && property.CurrentValue is not null;
    }

    private static bool IsEffectProperty(IProperty property)
    {
        return typeof(FilterEffect).IsAssignableFrom(property.ValueType)
               && property.CurrentValue is not null;
    }
}
