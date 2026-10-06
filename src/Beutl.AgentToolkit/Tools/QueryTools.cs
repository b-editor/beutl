using System.Collections;
using System.Collections.Immutable;
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
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ReadDocumentResponse(JsonObject Document, string SchemaVersion);

public sealed record RecommendedSkill(
    string Name,
    string WhenToUse,
    string HowToLoad);

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
public sealed partial class QueryTools(AgentSessionManager sessions) : ToolBase
{
    private const string RawHttpNote = "Raw HTTP MCP responses are Server-Sent Events. Read the JSON from the data: line, then decode result.content[0].text as the tool JSON payload. When returnImageContent=true, subsequent content blocks may include image/png data for visual review. notifications/initialized may return no body.";

    private readonly SchemaGenerator _schemaGenerator = new();
    private readonly CompositionTemplateCatalog _compositionCatalog = new();

    private static IReadOnlyList<RecommendedSkill> CreateRecommendedSkills()
        =>
        [
            new("beutl-agent-timeline-from-shotlist", "Editing timeline structure, timing, transforms, groups, and keyframes.", "Read the installed SKILL.md for the editing API contract."),
            new("beutl-agent-look-effect-chain", "Editing effect chains, masks, and shader properties.", "Read the installed SKILL.md for effect and shader mechanics."),
            new("beutl-agent-source-grounding", "Checking coordinates, units, bounds, and runtime behavior.", "Read the installed SKILL.md; source inspection is optional.")
        ];

    private static IReadOnlyDictionary<string, string> CreateCategoryAliases()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
        };

    [McpServerTool(Name = "get_started")]
    [Description("Returns the editing API workflow: attach/open, inspect schema and document data, apply edits, render, save, and export. Describes operation semantics only; it does not choose a creative direction or judge whether a video is finished.")]
    public ToolResult<GettingStartedResponse> GetStarted()
        => Execute(() => new GettingStartedResponse(
            SchemaVersion.Current,
            [
                "Call attach_active_editor for the open editor scene, or create_project/open_project for a file session. read_operation_status reports the session and persistence behavior.",
                "Read read_document_summary for scene and element handles; read_document returns the editable data. Use get_schema(type=...) for the specific types and properties needed by the edit.",
                "Use apply_edit with schemaVersion=1 and an Id-based merge patch. Unmentioned siblings are preserved; full desired documents are authoritative. undo/redo operate on the session history.",
                "Use natural, content-based element names in the user's language and compact ZIndex values. PortalObject.Count is a relative inclusive layer span and must remain consistent when layers move.",
                "Use measure_object_bounds for actual bounds and transforms. Default drawable alignment is centered; ScaleTransform uses percentages. Beutl source code is not required.",
                "Call validate_shader to check compilation. A successful compilation does not verify rendered output; render_still and render_storyboard return rendered evidence.",
                "measure_frame_differences reports numerical changes between frames; analyze_audio_rhythm reports estimated beats and onsets. Measurements are data, not quality or completion verdicts.",
                "For file-backed sessions, save_project persists edits. In LiveEditor sessions, use the editor's normal save behavior as reported by read_operation_status.",
                "export_video writes to the requested path. Successful validation, rendering, saving, or exporting confirms that operation only; the agent evaluates the result against the user's request."
            ],
            CreateRecommendedSkills(),
            CreateCategoryAliases(),
            RawHttpNote));

    [McpServerTool(Name = "get_schema")]
    [Description("Returns the capability schema for registered editable building blocks. After deciding the intended result, prefer type to check an unfamiliar type; use category when the type is unknown. A full catalog is optional, and the listed types can be composed into expressions with no named recipe. Category aliases such as visualEffect, effect, filter, videoEffect, text, typography, label, fill, stroke, and ease are accepted. Examples are opt-in; set includeExamples=true when snippets are needed.")]
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
                examples,
                includeStarters
                    ? "Starter examples are included because includeStarters=true. Use get_examples with a specific name and adapt the structure to the brief."
                    : "Full-scene starters are hidden by default. Use examples as small syntax snippets for your own concept; their coverage does not limit what you can compose with apply_edit.");
        });
    }

    [McpServerTool(Name = "get_examples")]
    [Description("Returns declarative patch examples without the full property schema. Pass a known name directly to fetch one snippet; use list_examples filtered by type or category only when you need to find a snippet. Examples illustrate syntax and do not limit possible compositions. Full-scene starters are hidden by default unless name is provided or includeStarters=true.")]
    public ToolResult<GetExamplesResponse> GetExamples(string? type = null, string? category = null, string? name = null, bool includeStarters = false)
    {
        return Execute(() => new GetExamplesResponse(
            SchemaVersion.Current,
            string.IsNullOrWhiteSpace(name)
                ? FilterExamples(_schemaGenerator.GenerateExamples(type, category, name), includeStarters)
                : _schemaGenerator.GenerateExamples(type, category, name),
            string.IsNullOrWhiteSpace(name) && !includeStarters
                ? "Full-scene starters are hidden by default. Pass name for an explicit starter, or includeStarters=true when the user asks for starters."
                : "Use name to fetch a single syntax example or starter. Examples can be adapted or combined through apply_edit."));
    }

    [McpServerTool(Name = "list_fonts")]
    [Description("Returns the font families this Beutl runtime has actually registered, with each available weight/style typeface pair. Font family resolution is by typographic family name: a subfamily such as \"Inter 28pt\" is not a family and will not match, and a family that is present may still lack the typeface pair you asked for. Call this before setting FontFamily/FontWeight/FontStyle rather than guessing from what is installed on the machine.")]
    public ToolResult<FontListResponse> ListFonts(
        [Description("Optional case-insensitive substring filter on the family name.")]
        string? nameFilter = null)
    {
        return Execute(() =>
        {
            FontFamilySummary[] families = FontManager.Instance.FontFamilies
                .Where(family => string.IsNullOrWhiteSpace(nameFilter)
                                 || family.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(family => family.Name, StringComparer.OrdinalIgnoreCase)
                .Select(family =>
                {
                    ImmutableArray<Typeface> typefaces = FontManager.Instance.GetTypefaces(family);
                    return new FontFamilySummary(
                        family.Name,
                        typefaces
                            .Distinct()
                            .OrderBy(item => item.Weight)
                            .ThenBy(item => item.Style)
                            .Select(item => new FontTypefaceSummary((int)item.Weight, item.Style.ToString()))
                            .ToArray());
                })
                .ToArray();

            return new FontListResponse(
                SchemaVersion.Current,
                families.Length,
                families,
                "Use the family Name verbatim as FontFamily, then choose Weight and Style together from one Typefaces entry. Pass the Weight integer as FontWeight and the Style name as FontStyle. An unavailable pair resolves to the nearest face rather than failing, so mixing values from different entries can render quietly with the wrong thickness or slant.");
        });
    }

    [McpServerTool(Name = "list_effects")]
    [Description("Returns Beutl FilterEffect building blocks with intent tags, property names, notes, and GPU requirements. Optional discovery for a specific implementation need: filter by intent after deciding the intended result, or go directly to get_schema for a known type. The list does not enumerate every look that combinations or custom scripts can produce.")]
    public ToolResult<ListEffectsResponse> ListEffects(string? intent = null, bool includePropertyNames = true)
    {
        return Execute(() => new ListEffectsResponse(
            SchemaVersion.Current,
            _schemaGenerator.ListEffects(intent, includePropertyNames),
            "Filter by intent such as glow, color, grade, glitch, outline, keying, motion, composite, gpu, or advanced. Call get_schema with type=<Name> for full property descriptors. An empty intent match only means no matching catalog tags; combine supported building blocks or investigate a custom script effect for the intended result."));
    }

    [McpServerTool(Name = "list_effect_recipes")]
    [Description("Returns optional effect recipe examples filtered by intent. Includes curated chains plus one single-effect recipe for every registered Beutl FilterEffect. Use when an implementation example would help; a missing recipe does not mean an expression is unsupported, and recipes are not a required step before editing.")]
    public ToolResult<ListEffectRecipesResponse> ListEffectRecipes(string? intent = null)
    {
        return Execute(() => new ListEffectRecipesResponse(
            SchemaVersion.Current,
            _schemaGenerator.ListEffectRecipes(intent),
            "If a recipe helps your concept, call get_effect_recipe with its name, adapt the patch, and replace placeholder element/drawable Ids before apply_edit. Otherwise compose your own supported geometry, masks, animation, effect chain, or script; an empty recipe match is not a capability limit."));
    }

    [McpServerTool(Name = "get_effect_recipe")]
    [Description("Returns an optional declarative patch example for a visual effect chain or a single Beutl FilterEffect. Pass a known recipe name directly or an intent tag; list_effect_recipes can help find an unknown name. Adapt the example to the intended result or author your own chain from supported types.")]
    public ToolResult<GetEffectRecipeResponse> GetEffectRecipe(string? name = null, string? intent = null)
    {
        return Execute(() => new GetEffectRecipeResponse(
            SchemaVersion.Current,
            _schemaGenerator.GetEffectRecipe(name, intent),
            "Replace <element-id> and <drawable-id> with Ids from read_document/read_document_summary, then pass recipe.patch to apply_edit with schemaVersion=1."));
    }

    [McpServerTool(Name = "validate_shader")]
    [Description("Compiles a candidate script for a script-compilable FilterEffect (SKSLScriptEffect, GLSLScriptEffect, CSharpScriptEffect) WITHOUT rendering, so shader edits can be checked before apply_edit. Pass the effect type name (e.g. SKSLScriptEffect) and the script text. status is one of: compiled; failed (error holds the compiler message); unavailable (compilation needs a graphics context absent in this session, e.g. headless GLSL — verify with render_still instead); unknown_type; not_script_effect. Call get_schema with type=<effect> first for the default script and its uniform list.")]
    public ToolResult<ValidateShaderResponse> ValidateShader(string effectType, string script)
    {
        return Execute(() =>
        {
            ShaderCompilationCheck check = _schemaGenerator.ValidateShader(effectType, script);
            string hint = check.Status switch
            {
                "compiled" => "The script compiled. Place it in the effect's script property via apply_edit.",
                "failed" => "Fix the reported compiler error, then validate again before apply_edit.",
                "unavailable" => "Compilation could not be attempted here (no graphics context). Validate GLSL inside the in-app editor session, or apply it and verify with render_still.",
                "unknown_type" => "Pass a FilterEffect type name from list_effects, such as SKSLScriptEffect.",
                "not_script_effect" => "Only SKSLScriptEffect, GLSLScriptEffect, and CSharpScriptEffect accept a script.",
                _ => string.Empty,
            };
            return new ValidateShaderResponse(SchemaVersion.Current, check.EffectType, check.Status, check.Error, hint);
        });
    }

    [McpServerTool(Name = "list_compositions")]
    [Description("Lists reusable named composition presets. These are optional editing aids; any returned name can be used or repeated.")]
    public ToolResult<ListCompositionsResponse> ListCompositions(string? tag = null, string? seed = null)
    {
        return Execute(() =>
        {
            CompositionTemplateList list = _compositionCatalog.List(tag, sessions.ResolveCompositionSeed(seed));
            return new ListCompositionsResponse(SchemaVersion.Current, list.Seed, list.Compositions);
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
        string? seed = null)
    {
        return Execute(() =>
        {
            RequireCompositionName(name);
            CompositionRender composition = _compositionCatalog.Render(
                name,
                tag,
                inputProps,
                sessions.ResolveCompositionSeed(seed));
            composition = composition with
            {
                Patch = CompositionTemplateCatalog.OffsetPatchElementStarts(composition.Patch, GetSceneStart())
            };
            return new RenderCompositionPatchResponse(
                SchemaVersion.Current,
                composition,
                "Pass composition.patch to apply_edit with schemaVersion=1. Use the returned seed to reproduce or intentionally vary this named template.");
        });
    }

    private TimeSpan GetSceneStart()
    {
        IEditingSession? session = sessions.CurrentSession;
        return session?.ReadOnSession(() => session.Root is Scene scene ? scene.Start : TimeSpan.Zero)
               ?? TimeSpan.Zero;
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
}

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

public sealed record FontFamilySummary(
    string Name,
    IReadOnlyList<FontTypefaceSummary> Typefaces);

public sealed record FontTypefaceSummary(
    int Weight,
    string Style);

public sealed record FontListResponse(
    string SchemaVersion,
    int FamilyCount,
    IReadOnlyList<FontFamilySummary> Families,
    string UsageHint);

public sealed record GetCompositionResponse(
    string SchemaVersion,
    CompositionTemplateDetail Composition);

public sealed record GetEffectRecipeResponse(
    string SchemaVersion,
    EffectRecipe Recipe,
    string UsageHint);

public sealed record GetExamplesResponse(
    string SchemaVersion,
    IReadOnlyList<DeclarativeExample> Examples,
    string SelectionHint);

public sealed record GettingStartedResponse(
    string SchemaVersion,
    IReadOnlyList<string> Essentials,
    IReadOnlyList<RecommendedSkill> RecommendedSkills,
    IReadOnlyDictionary<string, string> CategoryAliases,
    string RawHttpNote);

public sealed record ListCompositionsResponse(
    string SchemaVersion,
    string Seed,
    IReadOnlyList<CompositionTemplateSummary> Compositions);

public sealed record ListEffectRecipesResponse(
    string SchemaVersion,
    IReadOnlyList<EffectRecipeSummary> Recipes,
    string SelectionHint);

public sealed record ListEffectsResponse(
    string SchemaVersion,
    IReadOnlyList<EffectSummary> Effects,
    string SelectionHint);

public sealed record ListExamplesResponse(
    string SchemaVersion,
    IReadOnlyList<DeclarativeExampleSummary> Examples,
    string SelectionHint);

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

public sealed record RenderCompositionPatchResponse(
    string SchemaVersion,
    CompositionRender Composition,
    string UsageHint);

public sealed record ValidateShaderResponse(
    string SchemaVersion,
    string EffectType,
    string Status,
    string? Error,
    string Hint);
