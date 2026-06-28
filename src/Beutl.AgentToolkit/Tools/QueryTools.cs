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
                "In live mode, call attach_active_editor before scene tools.",
                "Call read_document_summary to inspect progress without the full document.",
                "Call list_effects and list_effect_recipes to discover Beutl's visual effect palette before choosing a repeated look.",
                "For low-context motion graphics, call list_compositions without seed for a session-varied default order, then plan_composition/apply_composition with the returned seed when you need exact reproduction.",
                "Use render_composition_patch only when the client explicitly needs the generated patch JSON; plan_composition/apply_composition avoid huge raw HTTP payloads.",
                "Call list_examples to choose a starter; examples are shuffled so the first empty-scene option varies across repeated runs.",
                "Call get_examples with name=<selected-example> to fetch exactly one patch.",
                "For visible progress, apply large scenes in stages: background first, then motion elements, then text/effects.",
                "Call plan_edit with the selected patch and schemaVersion=1.",
                "Call apply_edit with plan_edit.expectedChangeSet.",
                "Call render_still or export_video to verify the result."
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

    [McpServerTool(Name = "get_schema")]
    [Description("Returns the capability schema for registered editable types, optionally filtered by type or category. Category aliases such as visualEffect, effect, filter, videoEffect, fill, stroke, and ease are accepted. Set includeProperties=false for a compact type/discriminator catalog, or includeExamples=false when examples would make the response too large.")]
    public ToolResult<CapabilitySchema> GetSchema(
        string? type = null,
        string? category = null,
        bool includeProperties = true,
        bool includeExamples = true)
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
    [Description("Returns compact example names, descriptions, categories, and tags without large patches. Use this before get_examples so repeated runs can pick different visual starters.")]
    public ToolResult<ListExamplesResponse> ListExamples(string? type = null, string? category = null)
    {
        return Execute(() =>
        {
            IReadOnlyList<DeclarativeExampleSummary> examples = _schemaGenerator.ListExamples(type, category);
            return new ListExamplesResponse(
                SchemaVersion.Current,
                Shuffle(examples),
                "Examples are shuffled for no-context runs. Pick the first suitable empty-scene starter unless the user asked for a specific style, then call get_examples with that name.");
        });
    }

    [McpServerTool(Name = "get_examples")]
    [Description("Returns reusable declarative patch examples without the full property schema. Prefer list_examples first, then pass name to fetch exactly one patch. Accepts the same type and category aliases as get_schema.")]
    public ToolResult<GetExamplesResponse> GetExamples(string? type = null, string? category = null, string? name = null)
    {
        return Execute(() => new GetExamplesResponse(
            SchemaVersion.Current,
            string.IsNullOrWhiteSpace(name)
                ? Shuffle(_schemaGenerator.GenerateExamples(type, category, name))
                : _schemaGenerator.GenerateExamples(type, category, name),
            "If more than one example is returned, the order is shuffled. Choose the first suitable empty-scene starter, or use name to fetch a single patch."));
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
    [Description("Returns compact Remotion-style composition templates. Templates include props, style axes, default metadata, and deterministic shuffled order. If seed is omitted, the active session default seed is used.")]
    public ToolResult<ListCompositionsResponse> ListCompositions(string? tag = null, string? seed = null)
    {
        return Execute(() =>
        {
            CompositionTemplateList list = _compositionCatalog.List(tag, sessions.ResolveCompositionSeed(seed));
            return new ListCompositionsResponse(
                SchemaVersion.Current,
                list.Seed,
                list.Compositions,
                "Compositions are shuffled by seed. When seed is omitted, the active session default seed is used; reuse the returned seed to reproduce the same order and composition output.");
        });
    }

    [McpServerTool(Name = "get_composition")]
    [Description("Returns one Remotion-style composition contract: defaultProps, prop descriptors, calculated default metadata, Sequence-like timing, transitions, tags, and style axes.")]
    public ToolResult<GetCompositionResponse> GetComposition(string name)
    {
        return Execute(() => new GetCompositionResponse(
            SchemaVersion.Current,
            _compositionCatalog.Get(name)));
    }

    [McpServerTool(Name = "render_composition_patch")]
    [Description("Materializes a Remotion-style composition into a declarative Beutl JSON Merge Patch. Pass name or tag, optional inputProps, and optional seed. The same name/inputProps/seed produce the same patch.")]
    public ToolResult<RenderCompositionPatchResponse> RenderCompositionPatch(
        string? name = null,
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null)
    {
        return Execute(() => new RenderCompositionPatchResponse(
            SchemaVersion.Current,
            _compositionCatalog.Render(name, tag, inputProps, sessions.ResolveCompositionSeed(seed)),
            "Pass composition.patch to plan_edit/apply_edit with schemaVersion=1. Use the returned seed to reproduce or intentionally vary the generated motion."));
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
