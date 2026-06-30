using System.Collections;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
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
    IReadOnlyList<CreativeInspirationSeed> InspirationSeeds,
    IReadOnlyList<string> CombinationRules,
    IReadOnlyList<string> OriginalityConstraints,
    IReadOnlyList<string> VariationPrompts,
    IReadOnlyList<string> OverusedMotifs,
    IReadOnlyList<string> WorkflowHints,
    IReadOnlyList<string> StyleGuardrails,
    IReadOnlyList<string> PaletteGuidelines,
    IReadOnlyList<string> TypographyGuidelines,
    IReadOnlyList<string> MotionGuidelines,
    string SelectionHint,
    CreativeDirectionSelectionTrace? SelectionTrace = null);

public sealed record CreativeDirectionSelectionTrace(
    int RequestIndex,
    int BaseOffset,
    int AppliedOffset,
    string SeedMaterial,
    IReadOnlyList<string> ReturnedSeedOrder,
    string RecordHint);

public sealed record CreativeInspirationSeed(
    string Name,
    string Category,
    IReadOnlyList<string> Evokes,
    IReadOnlyList<string> Transformations,
    IReadOnlyList<string> UsefulTools);

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

public sealed record ObjectBoundsMeasurementResponse(
    string SchemaVersion,
    string Session,
    string Source,
    string SceneId,
    int FrameWidth,
    int FrameHeight,
    ObjectBoundsPoint FrameCenter,
    string Time,
    bool TimeFiltered,
    string CoordinateSpace,
    string MeasurementNote,
    IReadOnlyList<ObjectBoundsMeasurement> Objects);

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
    IReadOnlyList<string> NestedAnimatedProperties,
    bool IsFallback = false,
    string? FallbackReason = null,
    string? FallbackTypeName = null,
    string? FallbackMessage = null);

public sealed record ObjectBoundsMeasurement(
    string ElementId,
    string ElementName,
    string ElementStart,
    string ElementLength,
    int ElementZIndex,
    string ObjectId,
    string ObjectName,
    string Type,
    bool IsEnabled,
    string AlignmentX,
    string AlignmentY,
    string MeasurementKind,
    ObjectBoundsRect LocalBounds,
    ObjectBoundsRect TransformedBounds,
    ObjectBoundsPoint Center,
    ObjectBoundsPoint? UserTranslate,
    ObjectTransformMatrix UserTransformMatrix,
    string? Note = null);

public sealed record ObjectBoundsRect(
    double Left,
    double Top,
    double Right,
    double Bottom,
    double Width,
    double Height);

public sealed record ObjectBoundsPoint(double X, double Y);

public sealed record ObjectTransformMatrix(
    double M11,
    double M12,
    double M13,
    double M21,
    double M22,
    double M23,
    double M31,
    double M32,
    double M33);

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
                "Call measure_object_bounds before positioning text, backing plates, or centered objects; default Drawable alignment is centered, so TranslateTransform(0, 0) means the object's center is at the frame center.",
                "For original creative briefs, call list_creative_directions, synthesize an original pitch from at least two inspiration seeds, read_document, and get_schema only for the drawable/effect types you need, then author a custom declarative patch instead of cloning a starter.",
                "Call list_effects and list_effect_recipes to discover Beutl's visual effect palette before choosing a repeated look; for organic heat/ink/glass/noise fields, consider an SKSLScriptEffect shader recipe instead of stacking only blurred gradients.",
                "For no-context motion graphics, avoid overused orbit/radar/map/signal/dashboard motifs unless the user asks for them.",
                "Before authoring, write a compact creative brief with objective, audience, emotional temperature, message hierarchy, palette roles, typography roles, motion phases, and effect purpose.",
                "For high-tempo 1.5s motion-graphics beats, keep hero text to 1-3 words, supporting labels to 2-4 word tokens, and use non-text visual density such as nodes, particles, strokes, texture, and accent motion.",
                "For 120-140 BPM briefs, convert the tempo into a beat grid before authoring: around 130 BPM, 1 beat is about 462 ms, 2 beats about 923 ms, and normal foreground beats should change every 1-2 beats.",
                "Design every shot around one primary focal point; use grouping, alignment, scale contrast, color contrast, and repetition to make supporting layers scan below it.",
                "Keep only one hero-scale text role per beat; make captions, labels, and texture text visibly quieter so evaluate_edit_quality does not report overloaded hierarchy.",
                "Use RectShape mostly for full-frame/background plates; prefer EllipseShape, RoundedRectShape, GeometryShape, media, strokes, or procedural texture for foreground accents. Name intent with tags like [role:background], [role:text-backing], or [role:decorative] so quality tools can classify plates correctly.",
                "Keep ordinary timeline Elements to one EngineObject. Multiple Objects in one Element are allowed only when the Element contains an IFlowOperator such as DrawableGroup, DrawableDecorator, SoundGroup, or Scene3D.",
                "Every large or animated foreground shape needs a clear role/purpose and motion intent in its Element/Object name; avoid unnamed decorative blobs or arbitrary moving shapes.",
                "For readable type, keep copy short or extend duration; verify contrast, explicit [role:text-backing] plates, and read time with render_still and evaluate_edit_quality.",
                "Use effects only when they serve material texture, hierarchy separation, transition energy, or text legibility; avoid dense decorative stacks with no named job.",
                "For visible progress, apply large scenes in stages that follow your synthesized pitch: background/surface first, then motion elements, then text/effects.",
                "Before a large edit or after an intermediate stage, call preview_quality_risks to catch document-only risks early; call suggest_quality_fixes when you need a minimal repair plan.",
                "For unconstrained creative briefs, keep project/video/still basenames neutral and record the synthesized pitch in notes instead of filenames.",
                "New timeline Elements need '$type': '[Beutl.ProjectSystem]:Element'. Existing Elements keep Id; genuinely new Elements and Objects omit Id. If you need structure only, fetch the targeted insert-new-element-skeleton example instead of a full-scene starter.",
                "Choose animation clock mode explicitly: UseGlobalClock=false uses Element-local KeyTime values in 00:00:00..Element.Length; UseGlobalClock=true uses scene timeline KeyTime values that should intersect the visible Element range.",
                "For explicit keyframes, call get_examples for animate-float-property-keyframes or insert-new-animated-text-keyframes and copy the concrete KeyFrameAnimation<T> and KeyFrame<T> discriminators instead of inventing animation type names.",
                "Call apply_edit with the custom patch and schemaVersion=1.",
                "For file-backed sessions, call save_project after each major successful apply_edit so partial progress is durable.",
                "For synthesized creative pitches, verify read_document_summary contains your own planned element names before rendering or exporting.",
                "Use list_compositions, get_composition, and plan_composition only when the user explicitly asks for a reusable template, starter, or named composition style.",
                "When using a composition template, call list_compositions, choose a specific returned name that matches the user's request, then pass that name to plan_composition and apply_composition with the returned planId.",
                "Use render_composition_patch only when the client explicitly needs the generated template patch JSON.",
                "Call list_examples/get_examples for small schema snippets or as a fallback when a user asks for an example; full-scene starters are hidden by default.",
                "Call final_preflight before export_video when available; otherwise call render_still for representative frames, record planned-element visibility/readability plus layer density/contrast, run evaluate_motion_variation, then run evaluate_edit_quality and resolve critical/major issues before export_video."
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
            IReadOnlyList<CreativeInspirationSeed> allSeeds = CreateInspirationSeeds();
            int requestIndex = sessions.NextCreativeDirectionRequestIndex();
            string seedMaterial = CreateCreativeSeedMaterial(brief);
            int baseOffset = ComputeCreativeSeedBaseOffset(seedMaterial, allSeeds.Count);
            int appliedOffset = allSeeds.Count == 0 ? 0 : (baseOffset + requestIndex) % allSeeds.Count;
            IReadOnlyList<CreativeInspirationSeed> inspirationSeeds = OrderCreativeInspirationSeeds(allSeeds, appliedOffset);
            string selectionHint = string.IsNullOrWhiteSpace(brief)
                ? "No brief was supplied. Pick at least two inspiration seeds from different categories, combine them into a new one-sentence pitch with a new title, then author that pitch with a custom patch. Returned order is seeded and is not a ranking."
                : $"Use the brief as a constraint, combine at least two inspiration seeds into a new pitch, and avoid copying returned seed names or starter scenes: {brief.Trim()}";
            var selectionTrace = new CreativeDirectionSelectionTrace(
                requestIndex,
                baseOffset,
                appliedOffset,
                seedMaterial,
                inspirationSeeds.Select(seed => seed.Name).ToArray(),
                "Record requestIndex, appliedOffset, seedMaterial, returnedSeedOrder, chosen seed names/categories, and the synthesized pitch in notes.md before editing.");

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
                inspirationSeeds,
                [
                    "Synthesize a new pitch from at least two inspiration seeds in different categories before writing any patch.",
                    "Write a one-line direction contract that names objective, audience, emotional temperature, message hierarchy, brand posture, and delivery surface.",
                    "Use one seed as the visual subject and a second seed as the motion, texture, palette, or editing rhythm.",
                    "Change at least one material, motion verb, or composition relation from every seed you use.",
                    "Write your own element/object names from the synthesized pitch; do not reuse returned seed names as scene names.",
                    "If the user supplied constraints, keep the constraints literal and treat the seeds as optional ways to make the result less generic."
                ],
                [
                    "Do not implement any returned seed as a complete scene.",
                    "Do not use returned seed names as the final concept name, Element/Object names, project filename, still filename, or video filename.",
                    "Do not copy the seed order as a layer order.",
                    "Do not use full-scene examples or composition templates unless the user explicitly asks for a starter/template.",
                    "Avoid pitches close to your last output and avoid the listed overused motifs unless the user explicitly asks for them."
                ],
                [
                    "Invert the seed relationship: make the supposed background become the active subject and the subject become texture.",
                    "Replace a literal material with an abstract geometry that only hints at it.",
                    "Swap the expected motion speed: make a violent idea move quietly or a quiet material move with sharp editorial cuts.",
                    "Move the focal point off-center and use cropped detail instead of showing the whole motif.",
                    "Turn a typographic idea into non-text marks, or let tiny captions become the main rhythm.",
                    "Use procedural texture for one broad surface, then contrast it with a small hand-authored detail layer."
                ],
                [
                    "orbit rings",
                    "radar sweeps",
                    "map or atlas labels",
                    "signal nodes",
                    "dashboard metric bars",
                    "dark teal background with cyan/magenta neon"
                ],
                [
                    "Do not default to the first returned seed. Compare at least two seeds, reject seeds close to your last output, and synthesize a new pitch before authoring.",
                    "Record the seed names/categories and your new pitch in notes before creating elements.",
                    "Map your synthesized pitch to named Element/Object entries before writing a patch.",
                    "Map BPM requests to beat-grid durations before writing a patch; for 120-140 BPM, use 1-2 beat foreground events and avoid long unbroken foreground holds.",
                    "Apply/save in small stages that follow your own scene plan; avoid one huge full-scene patch when staged progress is easier to inspect.",
                    "After read_document_summary, compare actual element names with your own scene plan and revise missing planned parts before rendering.",
                    "Check that each shot has one primary focal point and that supporting copy, marks, effects, and accents sit below it in the hierarchy.",
                    "Check read time before rendering: short-lived text should be short, split across beats, or held longer.",
                    "Name the job of every effect chain before applying it: material texture, hierarchy separation, transition energy, color grade, or text legibility.",
                    "For unconstrained briefs, keep project/video/still basenames neutral instead of naming files after a returned seed or synthesized pitch.",
                    "After rendering stills, record which planned elements are visible/readable in each still, plus whether each development/resolution frame has at least three visible layer types and readable text contrast; revise if it does not.",
                    "Use at least three timing phases and animate multiple property families, not only X position and opacity.",
                    "Use one EngineObject per ordinary Element; only IFlowOperator Elements such as DrawableGroup, DrawableDecorator, SoundGroup, or Scene3D may carry multiple Objects.",
                    "Give large or animated foreground shapes role/purpose/motion-intent names before authoring; delete shapes whose job cannot be named.",
                    "For organic abstract pitches, consider SKSLScriptEffect from list_effect_recipes(intent: 'shader organic') and verify the shader with render_still before export.",
                    "Choose animation clock mode intentionally: UseGlobalClock=false uses Element-local KeyTime values; UseGlobalClock=true uses scene-timeline KeyTime values that should intersect sampled frames.",
                    "Name the synthesized pitch in any notes or output summary before creating elements.",
                    "Use list_effects/list_effect_recipes for available effects, then build the scene with apply_edit.",
                    "Keep full-scene examples and composition templates for explicit template/starter requests only.",
                    "Verify at least three stills, run evaluate_motion_variation, and export a short video preview when the encoder is available."
                ],
                [
                    "Do not default to all-caps title locks; use Title Case or sentence case unless the brief explicitly asks for an all-caps mark.",
                    "Every shot should have one primary focal point; avoid making several type blocks, panels, and effects compete at the same visual weight.",
                    "Use RectShape primarily for full-frame plates or deliberately plain geometry. Prefer RoundedRectShape, EllipseShape, GeometryShape, media, strokes, or procedural texture for foreground structure.",
                    "Avoid unclear foreground shapes: a large or animated shape must expose a role and motion purpose in its Element/Object name.",
                    "Avoid repeated card surfaces with heavy shadows or blur; use flatter editorial plates, texture, line work, masks, or subtle depth instead.",
                    "When placing a backing plate behind text, create a named text/backing pair with matching Start/Length, centered transforms, and clear padding."
                ],
                [
                    "Assign explicit roles before authoring: background, text, accent, support, and shadow.",
                    "Use a neutral or muted base plus one saturated accent; avoid dark teal with cyan and magenta unless the user asks for that specific look.",
                    "Keep text and backing plates separated by luma, not just hue, and treat readable text contrast as a hard delivery requirement.",
                    "If three or more colors are highly saturated, mute at least one support color before exporting."
                ],
                [
                    "Use mixed case for titles and captions by default.",
                    "Assign type roles before authoring: hero message, secondary emphasis, caption, label, and texture text.",
                    "Keep letter spacing modest; reserve wide tracking for one short label only.",
                    "Scale copy to duration: a fast beat should carry a word, short phrase, or symbol, not a full sentence.",
                    "Do not use slash-delimited technical captions as filler copy.",
                    "Check text readability in render_still and evaluate_edit_quality, especially when a backing plate is present."
                ],
                [
                    "Plan at least three phases: reveal, development, and resolution.",
                    "For 120-140 BPM, work from a beat grid rather than vague fast/slow language; at 130 BPM one beat is about 462 ms and two beats are about 923 ms.",
                    "Keep normal foreground holds near 2-4 beats, reserve longer holds for background texture or named final resolves, and add interstitial accents when text must stay readable.",
                    "Build fast tempo through contrast between quick accents and held readability beats, not by making every layer move at the same speed.",
                    "Bridge shot boundaries with overlap, opacity, transform continuation, or an intentional beat; avoid repeated unmotivated hard cuts.",
                    "Animate more than one property family across the piece.",
                    "After still checks, run evaluate_motion_variation and evaluate_edit_quality; do not export while critical or major issues remain."
                ],
                selectionHint,
                selectionTrace);
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
                string? hint = IsElementSchemaRequest(type, category)
                    ? "Timeline Element is a project-system container, not a capability-schema EngineObject. Use '$type': '[Beutl.ProjectSystem]:Element' only for new entries in Elements; put concrete drawable/effect/brush/transform objects under Objects using discriminators returned by get_schema. For structure, call get_examples(name: 'insert-new-element-skeleton')."
                    : null;
                throw new ReconcileException(new ToolError(
                    ErrorCode.UnknownType,
                    $"No schema entries matched type='{type}' category='{category}'.",
                    type ?? category,
                    hint));
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
                    : "Full-scene starters are hidden by default. Use examples as small snippets; for original briefs, call list_creative_directions, synthesize a pitch, and author a custom patch for apply_edit.");
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
            "Call get_effect_recipe with a recipe name, then apply the returned patch through apply_edit after replacing placeholder element/drawable Ids."));
    }

    [McpServerTool(Name = "get_effect_recipe")]
    [Description("Returns a declarative patch recipe for a visual effect chain or a single Beutl FilterEffect. Pass name from list_effect_recipes or an intent tag.")]
    public ToolResult<GetEffectRecipeResponse> GetEffectRecipe(string? name = null, string? intent = null)
    {
        return Execute(() => new GetEffectRecipeResponse(
            SchemaVersion.Current,
            _schemaGenerator.GetEffectRecipe(name, intent),
            "Replace <element-id> and <drawable-id> with Ids from read_document/read_document_summary, then pass recipe.patch to apply_edit with schemaVersion=1."));
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
                    : "Compositions are shuffled by seed, then recently applied or pre-attach previewed names are moved to the end when avoidRecent=true. Use a specific returned name only when the user explicitly asked for a template/starter; original creative briefs should use custom apply_edit patches. Explicitly selecting an avoided name is rejected unless avoidRecent=false.");
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
                "Pass composition.patch to apply_edit with schemaVersion=1. Use the returned seed to reproduce or intentionally vary this named template.");
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
            "Call list_compositions to inspect options, then pass a returned name only when the user explicitly asked for a reusable template/starter. For original creative briefs, author a custom patch for apply_edit."));
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

    private static bool IsElementSchemaRequest(string? type, string? category)
    {
        return IsElementText(type) || IsElementText(category);
    }

    private static bool IsElementText(string? value)
    {
        return string.Equals(value, nameof(Element), StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, typeof(Element).FullName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "[Beutl.ProjectSystem]:Element", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "timeline element", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "project element", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CreativeInspirationSeed> OrderCreativeInspirationSeeds(
        IReadOnlyList<CreativeInspirationSeed> seeds,
        int offset)
    {
        if (seeds.Count <= 1)
        {
            return seeds.ToArray();
        }

        var ordered = new CreativeInspirationSeed[seeds.Count];
        for (int i = 0; i < ordered.Length; i++)
        {
            ordered[i] = seeds[(i + offset) % seeds.Count];
        }

        return ordered;
    }

    private static string CreateCreativeSeedMaterial(string? brief)
    {
        return string.IsNullOrWhiteSpace(brief)
            ? (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60).ToString()
            : brief.Trim();
    }

    private static int ComputeCreativeSeedBaseOffset(string seedMaterial, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (char ch in seedMaterial)
            {
                hash ^= char.ToUpperInvariant(ch);
                hash *= 16777619;
            }

            return (int)(hash % (uint)count);
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

    [McpServerTool(Name = "measure_object_bounds")]
    [Description("Measures RenderNode operation bounds for Drawable objects in the current scene. Use before positioning text, backing plates, or centered objects; default Drawable TranslateTransform values are offsets from the alignment-resolved position, not top-left coordinates.")]
    public ToolResult<ObjectBoundsMeasurementResponse> MeasureObjectBounds(
        string? objectId = null,
        string? elementId = null,
        double? timeSeconds = null)
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

            Guid? objectGuid = ParseOptionalGuid(objectId, nameof(objectId));
            Guid? elementGuid = ParseOptionalGuid(elementId, nameof(elementId));
            TimeSpan time = ParseMeasurementTime(timeSeconds);
            bool timeFiltered = timeSeconds.HasValue;

            Element? selectedElement = null;
            if (elementGuid is { } elementGuidValue)
            {
                selectedElement = scene.Children.FirstOrDefault(item => item.Id == elementGuidValue);
                if (selectedElement is null)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.StaleHandle,
                        $"No Element with Id '{elementId}' exists in the current scene.",
                        elementId));
                }
            }

            Drawable? selectedDrawable = null;
            if (objectGuid is { } objectGuidValue)
            {
                var entity = IdentityHelper.FindById(scene, objectGuidValue);
                if (entity is null)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.StaleHandle,
                        $"No object with Id '{objectId}' exists in the current scene.",
                        objectId));
                }

                if (entity is not Drawable drawable)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        $"Object '{objectId}' is a {entity.GetType().FullName}, not a Drawable.",
                        objectId,
                        "Pass a Drawable object Id from read_document_summary or omit objectId to measure all direct Drawable objects."));
                }

                selectedDrawable = drawable;
            }

            Size canvasSize = new(scene.FrameSize.Width, scene.FrameSize.Height);
            var context = new CompositionContext(time);
            var measurements = new List<ObjectBoundsMeasurement>();
            foreach (Element element in scene.Children)
            {
                if (selectedElement is not null && element != selectedElement)
                {
                    continue;
                }

                if (timeFiltered && (!element.IsEnabled || !element.Range.Contains(time)))
                {
                    continue;
                }

                foreach (EngineObject obj in element.Objects)
                {
                    if (obj is not Drawable drawable)
                    {
                        continue;
                    }

                    if (selectedDrawable is not null && drawable != selectedDrawable)
                    {
                        continue;
                    }

                    if (timeFiltered && !drawable.IsEnabled)
                    {
                        continue;
                    }

                    measurements.Add(MeasureDrawable(element, drawable, canvasSize, context));
                }
            }

            if (selectedElement is not null && selectedDrawable is not null && measurements.Count == 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Drawable '{objectId}' is not a direct object of Element '{elementId}' at the requested time.",
                    objectId,
                    "Measure the object without elementId, or use the Element that directly contains the object."));
            }

            if (selectedDrawable is not null && measurements.Count == 0 && !timeFiltered)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Drawable '{objectId}' is not a direct object of any Element in the current scene.",
                    objectId,
                    "measure_object_bounds currently measures direct Drawable objects in timeline Elements. Nested flow/group drawables are reported as an unsupported improvement area."));
            }

            return new ObjectBoundsMeasurementResponse(
                SchemaVersion.Current,
                session.SessionId,
                session.Source.ToString(),
                scene.Id.ToString(),
                scene.FrameSize.Width,
                scene.FrameSize.Height,
                new ObjectBoundsPoint(scene.FrameSize.Width / 2d, scene.FrameSize.Height / 2d),
                time.ToString("c"),
                timeFiltered,
                "Scene pixel coordinates. TransformedBounds are authoritative axis-aligned scene-space bounds measured from RenderNodeOperation.Bounds. LocalBounds are normalized from the render-node extents for size only and are not Drawable.MeasureCore results.",
                "Default Drawable AlignmentX/AlignmentY is Center, so a pure TranslateTransform(x, y) moves the object relative to the alignment-resolved position. For a centered object in a 1920x1080 scene, TranslateTransform(0, 0) centers it at (960, 540). Bounds are measured through DrawableRenderNode and RenderNodeProcessor rather than per-type Drawable.Measure/FilterEffect.TransformBounds estimates.",
                measurements);
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

    private static IReadOnlyList<CreativeInspirationSeed> CreateInspirationSeeds()
    {
        return
        [
            new CreativeInspirationSeed(
                "translucent archive paper",
                "material",
                [
                    "fibers, vellum, partial opacity, torn edges",
                    "quiet historical texture without becoming a literal scrapbook"
                ],
                [
                    "make it a mask instead of a background",
                    "replace torn edges with cropped geometric planes",
                    "let the paper texture drive typography opacity"
                ],
                [
                    "RectShape",
                    "LinearGradientBrush",
                    "Opacity",
                    "DropShadow"
                ]),
            new CreativeInspirationSeed(
                "magnetic letter fragments",
                "typography",
                [
                    "broken glyphs, attraction, tension lines, interrupted readability",
                    "type that behaves like matter instead of a static label"
                ],
                [
                    "make the fragments non-letter shapes until the last third",
                    "use connector lines as the main motion rather than decoration",
                    "settle into an unreadable mark instead of a literal headline"
                ],
                [
                    "TextBlock",
                    "RectShape",
                    "TextBlock.Spacing",
                    "TranslateTransform"
                ]),
            new CreativeInspirationSeed(
                "thermal afterimage",
                "palette",
                [
                    "heat traces, false color, delayed glow, cooling edges",
                    "color that reveals time rather than just filling space"
                ],
                [
                    "remove the expected black background",
                    "make the hottest color the smallest object",
                    "use thermal color on typography instead of blobs"
                ],
                [
                    "SKSLScriptEffect",
                    "Blur",
                    "Brightness",
                    "LinearGradientBrush"
                ]),
            new CreativeInspirationSeed(
                "off-axis glass refraction",
                "material",
                [
                    "split highlights, transparent slabs, displaced edges, caustic streaks",
                    "image layers that feel bent without showing a literal prism"
                ],
                [
                    "make refraction visible only where text overlaps it",
                    "replace slabs with narrow moving seams",
                    "contrast a hard caustic line against soft grain"
                ],
                [
                    "RectShape",
                    "LinearGradientBrush",
                    "Blur",
                    "DropShadow"
                ]),
            new CreativeInspirationSeed(
                "misregistered print pressure",
                "composition",
                [
                    "offset plates, overprint seams, halftone density, crop pressure",
                    "editorial composition where errors become the motion"
                ],
                [
                    "use only one printed color plus a foreign accent color",
                    "make the registration error grow worse instead of resolving",
                    "turn crop marks into a moving frame rather than corner details"
                ],
                [
                    "RectShape",
                    "EllipseShape",
                    "Brightness",
                    "TextBlock.Spacing"
                ]),
            new CreativeInspirationSeed(
                "liquid glaze memory",
                "motion",
                [
                    "spreading puddles, hairline cracks, slow cooling, glossy islands",
                    "motion that feels viscous before it becomes crisp"
                ],
                [
                    "make cracks appear before the liquid surface",
                    "let the liquid motion drive a cropped title reveal",
                    "replace puddles with bands or narrow seams"
                ],
                [
                    "EllipseShape",
                    "RectShape",
                    "Blur",
                    "Brightness"
                ]),
            new CreativeInspirationSeed(
                "handheld calibration marks",
                "detail density",
                [
                    "ticks, crop marks, handwritten offsets, imperfect measuring systems",
                    "small marks that make the frame feel authored but not like a dashboard"
                ],
                [
                    "make the calibration marks drift out of calibration",
                    "use marks as masks for other layers",
                    "hide the marks until the final third so they reframe the image"
                ],
                [
                    "RectShape",
                    "TextBlock",
                    "Opacity",
                    "TranslateTransform"
                ]),
            new CreativeInspirationSeed(
                "cropped macro aperture",
                "composition",
                [
                    "extreme crop, frame-within-frame, partial visibility, negative-space pressure",
                    "a subject that is never fully shown"
                ],
                [
                    "keep the most important object off-screen",
                    "make the crop window move instead of the subject",
                    "resolve to a detail, not a wide shot"
                ],
                [
                    "RectShape",
                    "ScaleTransform",
                    "TranslateTransform",
                    "Opacity"
                ]),
            new CreativeInspirationSeed(
                "phosphor decay",
                "procedural surface",
                [
                    "scan persistence, fading trails, soft electronic residue, pixel memory",
                    "time-lag texture without defaulting to neon dashboards"
                ],
                [
                    "apply it to warm daylight colors instead of cyan/magenta",
                    "make the decay visible only after cuts",
                    "use one procedural surface as a memory layer behind physical materials"
                ],
                [
                    "SKSLScriptEffect",
                    "Brightness",
                    "Blur",
                    "Opacity"
                ])
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

    private static Guid? ParseOptionalGuid(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out Guid id))
        {
            return id;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"{parameterName} must be a GUID.",
            value));
    }

    private static TimeSpan ParseMeasurementTime(double? timeSeconds)
    {
        if (timeSeconds is not { } seconds)
        {
            return TimeSpan.Zero;
        }

        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "timeSeconds must be a finite non-negative number.",
                seconds.ToString("R")));
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static ObjectBoundsMeasurement MeasureDrawable(
        Element element,
        Drawable drawable,
        Size canvasSize,
        CompositionContext context)
    {
        RenderNodeBounds renderNodeBounds = MeasureDrawableRenderNodeBounds(drawable, canvasSize, context);
        Rect transformedBounds = renderNodeBounds.Bounds;
        Rect localBounds = NormalizeBoundsSize(transformedBounds);
        AlignmentX alignmentX = context.Get(drawable.AlignmentX);
        AlignmentY alignmentY = context.Get(drawable.AlignmentY);
        Transform? transform = context.Get(drawable.Transform);
        Matrix userTransform = transform?.CreateMatrix(context) ?? Matrix.Identity;
        ObjectBoundsPoint? userTranslate = userTransform.TryDecomposeTransform(out Vector translate, out _, out _, out _)
            ? new ObjectBoundsPoint(translate.X, translate.Y)
            : null;
        string? note = renderNodeBounds.Note is null
            ? "Measured through DrawableRenderNode and RenderNodeProcessor.PullToRoot(). LocalBounds is normalized from render-node extents for size only."
            : $"{renderNodeBounds.Note} LocalBounds is normalized from render-node extents for size only.";

        return new ObjectBoundsMeasurement(
            element.Id.ToString(),
            element.Name,
            element.Start.ToString("c"),
            element.Length.ToString("c"),
            element.ZIndex,
            drawable.Id.ToString(),
            drawable.Name,
            drawable.GetType().FullName ?? drawable.GetType().Name,
            drawable.IsEnabled,
            alignmentX.ToString(),
            alignmentY.ToString(),
            "render-node-operation-bounds",
            ToBoundsRect(localBounds),
            ToBoundsRect(transformedBounds),
            ToBoundsPoint(transformedBounds.Center),
            userTranslate,
            ToTransformMatrix(userTransform),
            note);
    }

    private static RenderNodeBounds MeasureDrawableRenderNodeBounds(
        Drawable drawable,
        Size canvasSize,
        CompositionContext context)
    {
        using var resource = (Drawable.Resource)drawable.ToResource(context);
        using var node = new DrawableRenderNode(resource);
        using (var graphicsContext = new GraphicsContext2D(node, canvasSize, outputScale: 1f))
        {
            drawable.Render(graphicsContext, resource);
        }

        var processor = new RenderNodeProcessor(node, useRenderCache: false, outputScale: 1f, maxWorkingScale: 1f);
        RenderNodeOperation[] operations = processor.PullToRoot();
        Rect bounds = Rect.Empty;
        bool hasBounds = false;
        try
        {
            foreach (RenderNodeOperation operation in operations)
            {
                Rect operationBounds = operation.Bounds;
                bounds = hasBounds ? bounds.Union(operationBounds) : operationBounds;
                hasBounds = true;
            }
        }
        finally
        {
            DisposeRenderNodeOperations(operations);
        }

        return hasBounds
            ? new RenderNodeBounds(bounds, null)
            : new RenderNodeBounds(Rect.Empty, "The drawable produced no RenderNode operations at the requested time.");
    }

    private static void DisposeRenderNodeOperations(RenderNodeOperation[] operations)
    {
        foreach (RenderNodeOperation operation in operations)
        {
            try
            {
                operation.Dispose();
            }
            catch
            {
                // Match renderer cleanup behavior: disposal faults must not hide measurement results.
            }
        }
    }

    private static Rect NormalizeBoundsSize(Rect bounds)
    {
        return new Rect(0, 0, MathF.Max(0f, bounds.Width), MathF.Max(0f, bounds.Height));
    }

    private sealed record RenderNodeBounds(Rect Bounds, string? Note);

    private static ObjectBoundsRect ToBoundsRect(Rect rect)
    {
        return new ObjectBoundsRect(rect.Left, rect.Top, rect.Right, rect.Bottom, rect.Width, rect.Height);
    }

    private static ObjectBoundsPoint ToBoundsPoint(Point point)
    {
        return new ObjectBoundsPoint(point.X, point.Y);
    }

    private static ObjectTransformMatrix ToTransformMatrix(Matrix matrix)
    {
        return new ObjectTransformMatrix(
            matrix.M11,
            matrix.M12,
            matrix.M13,
            matrix.M21,
            matrix.M22,
            matrix.M23,
            matrix.M31,
            matrix.M32,
            matrix.M33);
    }

    private static ObjectSummary CreateObjectSummary(EngineObject obj)
    {
        IProperty[] properties = obj.Properties.ToArray();
        bool isFallback = obj is IFallback;
        string? fallbackReason = null;
        string? fallbackTypeName = null;
        string? fallbackMessage = null;
        if (obj is IFallback fallback)
        {
            fallbackReason = fallback.Reason.ToString();
            fallback.TryGetTypeName(out fallbackTypeName);
            fallbackMessage = fallback.ErrorMessage;
        }

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
                .ToArray(),
            isFallback,
            fallbackReason,
            fallbackTypeName,
            fallbackMessage);
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
