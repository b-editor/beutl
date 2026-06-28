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

public sealed record GetExamplesResponse(string SchemaVersion, IReadOnlyList<DeclarativeExample> Examples);

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
    IReadOnlyList<string> EffectProperties);

[McpServerToolType]
public sealed class QueryTools(AgentSessionManager sessions) : ToolBase
{
    private readonly SchemaGenerator _schemaGenerator = new();

    [McpServerTool(Name = "get_started")]
    [Description("Returns a compact, low-context guide for using the Beutl Agent Editing Toolkit. Use this first when an agent only has the MCP endpoint URL.")]
    public ToolResult<GettingStartedResponse> GetStarted()
    {
        return Execute(() => new GettingStartedResponse(
            SchemaVersion.Current,
            [
                "In live mode, call attach_active_editor before scene tools.",
                "Call read_document_summary to inspect progress without the full document.",
                "Call get_examples with type=TextBlock to get create-empty-scene-motion-graphics.",
                "Call plan_edit with the example patch and schemaVersion=1.",
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
            "Raw HTTP MCP responses are Server-Sent Events. Read the JSON from the data: line. notifications/initialized may return no body."));
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

    [McpServerTool(Name = "get_examples")]
    [Description("Returns reusable declarative patch examples without the full property schema. Use this first when starting from an empty scene or when get_schema would be too large. Accepts the same type and category aliases as get_schema.")]
    public ToolResult<GetExamplesResponse> GetExamples(string? type = null, string? category = null)
    {
        return Execute(() => new GetExamplesResponse(
            SchemaVersion.Current,
            _schemaGenerator.GenerateExamples(type, category)));
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
            properties.Where(IsEffectProperty).Select(property => property.Name).ToArray());
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
