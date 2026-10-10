using System.Collections;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class QueryTools
{
    [McpServerTool(Name = "read_document_summary")]
    [Description("Reads a compact summary of the current scene without returning the full declarative JSON. Use this for live progress observation or before deciding whether a full read_document call is necessary.")]
    public ToolResult<DocumentSummaryResponse> ReadDocumentSummary()
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            // Read/traverse the live scene on the session dispatcher so a LiveEditor summary does not
            // race UI-thread edits to scene.Children.
            return session.ReadOnSession(() =>
            {
                Scene scene = RequireSceneRoot(session);
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
        });
    }

    [McpServerTool(Name = "read_document")]
    [Description("Reads the current declarative document, or a subtree selected by rootId. This can be large; use read_document_summary for progress checks. In live MCP, pass sceneId on each call; in the stdio host, call open_project or create_project first.")]
    public ToolResult<ReadDocumentResponse> ReadDocument(string? rootId = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            // Live sessions own their scene on the UI thread, so read and serialize on the dispatcher.
            JsonObject document = session.ReadOnSession(() => ReadDocumentBody(session, rootId));
            return new ReadDocumentResponse(document, SchemaVersion.Current);
        });
    }

    private static JsonObject ReadDocumentBody(IEditingSession session, string? rootId)
    {
        if (string.IsNullOrWhiteSpace(rootId))
        {
            return session.Documents.Read(session.Root);
        }

        if (Guid.TryParse(rootId, out Guid id)
            && IdentityHelper.FindById(session.Root, id) is CoreObject subtree)
        {
            JsonObject document = CoreSerializer.SerializeToJsonObject(
                subtree,
                new CoreSerializerOptions
                {
                    BaseUri = subtree.Uri,
                    Mode = CoreSerializationMode.EmbedReferencedObjects
                });
            SchemaVersion.Stamp(document);
            return document;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.StaleHandle,
            $"No entity with Id '{rootId}' exists in the current session.",
            rootId));
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
                        foreach (string child in CreateNestedAnimatedPropertySummaries(propertyPath, listProperty, visited))
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
