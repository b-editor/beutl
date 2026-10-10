using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

internal static class AgentHostSceneRouter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> s_required = new(StringComparer.Ordinal)
    {
        "read_document_summary", "read_document", "apply_edit", "duplicate_object",
        "plan_composition", "apply_composition", "measure_object_bounds",
        "undo", "redo", "read_history", "render_still", "render_storyboard",
        "measure_frame_differences", "export_video", "add_scene", "save_project"
    };

    private static readonly HashSet<string> s_optional = new(StringComparer.Ordinal)
    {
        "read_operation_status", "list_compositions", "render_composition_patch"
    };

    public static void AddFilters(IMcpRequestFilterBuilder filters)
    {
        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            ListToolsResult result = await next(context, cancellationToken).ConfigureAwait(false);
            result.Tools = result.Tools.Select(tool =>
            {
                if (!s_required.Contains(tool.Name) && !s_optional.Contains(tool.Name))
                    return tool;

                // The SDK caches local schemas. Publish a clone with the host-specific target.
                JsonObject node = JsonSerializer.SerializeToNode(tool)!.AsObject();
                JsonObject schema = node["inputSchema"]!.AsObject();
                var properties = schema["properties"] as JsonObject;
                if (properties is null)
                    schema["properties"] = properties = new JsonObject();
                properties.Remove("session");
                properties["sceneId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Target scene ID from list_scenes or open_project/create_project/add_scene. Pass it on every scene call. The visible editor selection stays unchanged. Closed scenes receive an unselected background tab for normal history and save lifecycle; no attach or shared selected scene is used."
                };
                var required = schema["required"] as JsonArray;
                if (required is not null)
                {
                    JsonNode? session = required.FirstOrDefault(item => item?.GetValue<string>() == "session");
                    if (session is not null)
                        required.Remove(session);
                }
                if (s_required.Contains(tool.Name))
                {
                    if (required is null)
                        schema["required"] = required = new JsonArray();
                    required.Add("sceneId");
                }
                return node.Deserialize<Tool>()!;
            }).ToArray();
            return result;
        });

        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            CallToolRequestParams original = context.Params;
            bool required = s_required.Contains(original.Name);
            if (!required && !s_optional.Contains(original.Name))
                return await next(context, cancellationToken).ConfigureAwait(false);

            if (original.Arguments is not { } arguments || !arguments.TryGetValue("sceneId", out JsonElement id))
                return required ? InvalidTarget() : await next(context, cancellationToken).ConfigureAwait(false);
            if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out Guid sceneId)
                || sceneId == Guid.Empty)
                return InvalidTarget();

            // Session handles are not part of the live API, including legacy add_scene/save_project.
            if (arguments.ContainsKey("session"))
                return InvalidTarget("Live MCP uses sceneId on each call; remove the session argument.");

            var localArguments = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
            localArguments.Remove("sceneId");
            if (original.Name == "add_scene")
                localArguments["session"] = JsonSerializer.SerializeToElement(sceneId.ToString());

            IServiceProvider services = context.Services!;
            var sessions = services.GetRequiredService<AgentSessionManager>();
            var gateway = services.GetRequiredService<EditorProjectSessionGateway>();
            sessions.UseRequestTarget(() => gateway.ResolveScene(sceneId));
            context.Params = new CallToolRequestParams
            {
                Name = original.Name,
                Arguments = localArguments,
                Meta = original.Meta,
                InputResponses = original.InputResponses,
                RequestState = original.RequestState
            };
            try { return await next(context, cancellationToken).ConfigureAwait(false); }
            finally { context.Params = original; }
        });
    }

    private static CallToolResult InvalidTarget(string? message = null)
        => new()
        {
            Content = [new TextContentBlock
            {
                Text = JsonSerializer.Serialize(ToolResult<object?>.Failure(
                    ErrorCode.ValidationRejected,
                    message ?? "Pass a valid sceneId on every scene tool call.",
                    "sceneId", "Call list_scenes on the target instance to discover scene IDs."), s_jsonOptions)
            }]
        };
}
