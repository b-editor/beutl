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
    IReadOnlyList<string> OverusedMotifs,
    IReadOnlyList<string> WorkflowHints,
    string SelectionHint);

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
                "In live mode, call attach_active_editor before scene tools. If it fails, open or create a project/scene in Beutl, then call attach_active_editor again.",
                "In stdio/headless mode, call create_project or open_project instead of writing a one-off generator.",
                "Call read_document_summary to inspect progress without the full document.",
                "For original creative briefs, call list_creative_directions, read_document, and get_schema only for the drawable/effect types you need, then author a custom declarative patch instead of cloning a starter.",
                "Call list_effects and list_effect_recipes to discover Beutl's visual effect palette before choosing a repeated look.",
                "For no-context motion graphics, avoid overused orbit/radar/map/signal/dashboard motifs unless the user asks for them.",
                "For visible progress, apply large scenes in stages: background first, then motion elements, then text/effects.",
                "Call plan_edit with the custom patch and schemaVersion=1.",
                "Call apply_edit with plan_edit.expectedChangeSet.",
                "Use list_compositions, get_composition, and plan_composition only when the user explicitly asks for a reusable template, starter, or named composition style.",
                "When using a composition template, call list_compositions, choose a specific returned name that matches the user's request, then pass that name to plan_composition and apply_composition with the returned planId.",
                "Use render_composition_patch only when the client explicitly needs the generated template patch JSON.",
                "Call list_examples/get_examples for small schema snippets or as a fallback when a user asks for an example; full-scene starters are hidden by default.",
                "Call render_still for representative frames and export_video for a finished motion preview when an encoder is available."
            ],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["visualEffect"] = "FilterEffect",
                ["effect"] = "FilterEffect",
                ["filter"] = "FilterEffect",
                ["videoEffect"] = "FilterEffect",
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
        return Execute(() => new CreativeDirectionResponse(
            SchemaVersion.Current,
            [
                "material: paper cutouts, ink bleed, glass refraction, fabric folds, projected light, ceramic glaze, thermal camera, risograph print, CRT phosphor, blueprint pencil",
                "motion: bloom, peel, fold, pour, shatter, magnetic attraction, wave interference, hand-drawn reveal, parallax drift, stop-motion stepping",
                "composition: macro close-up, off-axis crop, split depth planes, negative-space title, diagonal editorial grid, frame-within-frame, vertical poster stack",
                "typography: small caption system, kinetic word fragments, numeric countdown, subtitle-only rhythm, oversized cropped letterforms",
                "palette: warm/cool clash, muted paper plus neon accent, monochrome with one warning color, daylight pastels, high-contrast print inks"
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
                "Pick two direction axes and one contrast axis before writing a patch.",
                "Name the concept in any notes or output summary before creating elements.",
                "Use list_effects/list_effect_recipes for available effects, then build the scene with plan_edit/apply_edit.",
                "Keep full-scene examples and composition templates for explicit template/starter requests only.",
                "Verify at least three stills and export a short video preview when the encoder is available."
            ],
            string.IsNullOrWhiteSpace(brief)
                ? "No brief was supplied. Choose a direction that avoids the overused motifs and write a one-sentence concept before editing."
                : $"Use these axes to reinterpret the brief without copying starter scenes: {brief.Trim()}"));
    }

    [McpServerTool(Name = "get_schema")]
    [Description("Returns the capability schema for registered editable types, optionally filtered by type or category. Category aliases such as visualEffect, effect, filter, videoEffect, fill, stroke, and ease are accepted. Examples are opt-in so creative briefs do not anchor on starter scenes; set includeExamples=true when snippets are explicitly needed.")]
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
