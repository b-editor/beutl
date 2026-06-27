using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ReadDocumentResponse(JsonObject Document, string SchemaVersion);

[McpServerToolType]
public sealed class QueryTools(AgentSessionManager sessions) : ToolBase
{
    private readonly SchemaGenerator _schemaGenerator = new();

    [McpServerTool(Name = "get_schema")]
    [Description("Returns the capability schema for registered editable types, optionally filtered by type or category.")]
    public ToolResult<CapabilitySchema> GetSchema(string? type = null, string? category = null)
    {
        return Execute(() =>
        {
            CapabilitySchema schema = _schemaGenerator.Generate(type, category);
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

    [McpServerTool(Name = "read_document")]
    [Description("Reads the current declarative document, or a subtree selected by rootId.")]
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
}
