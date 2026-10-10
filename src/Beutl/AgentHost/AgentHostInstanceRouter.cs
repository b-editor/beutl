using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.AgentToolkit.Live;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

// Routes a tool call carrying an instanceId to the right Beutl process of this profile. Discovery,
// identity proof and forwarding are the AgentHostPeerClient the live MCP broker uses as well.
internal sealed class AgentHostInstanceRouter
{
    internal const string InstanceHeader = AgentHostPeerClient.InstanceHeader;

    private readonly AgentHostInstanceRegistry _registry;
    private readonly ProjectService _projects;
    private readonly EditorService _editors;
    private readonly AgentHostInstanceAuthentication _authentication;
    private readonly AgentHostPeerClient _peers;

    public AgentHostInstanceRouter(
        AgentHostInstanceRegistry registry, ProjectService projects, EditorService editors,
        string token, string? instanceId = null)
    {
        _registry = registry;
        _projects = projects;
        _editors = editors;
        _authentication = new AgentHostInstanceAuthentication(token, instanceId ?? Guid.NewGuid().ToString("N"));
        _peers = new AgentHostPeerClient(registry, _authentication);
    }

    public string InstanceId => _authentication.InstanceId;

    public AgentHostIdentityProof CreateIdentityProof(string challenge) => _authentication.CreateProof(challenge);

    public bool IsForwardToken(string provided) => _authentication.IsForwardToken(provided);

    public async Task<AgentHostInstanceInfo> GetInfoAsync(CancellationToken cancellationToken)
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Project? project = _projects.CurrentProject.Value;
            var editor = _editors.SelectedTabItem.Value?.Context.Value as EditViewModel;
            string? projectPath = project?.Uri is { IsFile: true } uri ? uri.LocalPath : null;
            string? projectName = string.IsNullOrWhiteSpace(project?.Name)
                ? Path.GetFileNameWithoutExtension(projectPath)
                : project.Name;
            return new AgentHostInstanceInfo(
                InstanceId, Environment.ProcessId, projectName, projectPath,
                editor?.Scene.Id.ToString(), editor?.Scene.Name);
        }, DispatcherPriority.Normal, cancellationToken);

    public async Task<ListAgentHostInstancesResponse> ListAsync(CancellationToken cancellationToken)
    {
        AgentHostInstanceInfo self = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        Task<AgentHostInstanceInfo?>[] tasks = _registry.Read().Where(entry => entry.InstanceId != InstanceId)
            .Select(entry => _peers.ReadInfoAsync(entry, cancellationToken)).ToArray();
        AgentHostInstanceInfo?[] instances = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new ListAgentHostInstancesResponse(InstanceId, instances.OfType<AgentHostInstanceInfo>().Prepend(self)
            .OrderBy(info => info.ProcessId).ThenBy(info => info.InstanceId, StringComparer.Ordinal).ToArray());
    }

    public ValueTask<CallToolResult> ForwardAsync(
        string instanceId, CallToolRequestParams parameters, McpServer server, CancellationToken cancellationToken)
        => _peers.ForwardAsync(instanceId, parameters, server, cancellationToken);

    public static void AddFilters(IMcpRequestFilterBuilder filters)
    {
        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            ListToolsResult result = await next(context, cancellationToken).ConfigureAwait(false);
            // Clone protocol tools: they are cached by the SDK and must keep their local schemas.
            result.Tools = result.Tools.Select(tool =>
            {
                if (tool.Name == "list_instances")
                    return tool;

                JsonObject node = JsonSerializer.SerializeToNode(tool)!.AsObject();
                JsonObject schema = node["inputSchema"]!.AsObject();
                var properties = schema["properties"] as JsonObject;
                if (properties is null)
                    schema["properties"] = properties = new JsonObject();
                properties["instanceId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Target Beutl instance ID from list_instances. Pass the same ID on every call, including scene discovery, read, edit, history, render and job polling. Omit to operate on the connected instance."
                };
                return node.Deserialize<Tool>()!;
            }).ToArray();
            return result;
        });

        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            CallToolRequestParams original = context.Params;
            if (original?.Arguments is not { } arguments || !arguments.TryGetValue("instanceId", out JsonElement id))
                return await next(context, cancellationToken).ConfigureAwait(false);

            if (original.Name == "list_instances" || id.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(id.GetString(), "N", out _))
                return LiveToolResults.InvalidInstanceId();

            string instanceId = id.GetString()!;
            var forwardedArguments = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
            forwardedArguments.Remove("instanceId");
            var parameters = new CallToolRequestParams
            {
                Name = original.Name,
                Arguments = forwardedArguments,
                Meta = original.Meta,
                InputResponses = original.InputResponses,
                RequestState = original.RequestState
            };
            var router = context.Services!.GetRequiredService<AgentHostInstanceRouter>();
            if (instanceId != router.InstanceId)
                return await router.ForwardAsync(instanceId, parameters, context.Server, cancellationToken).ConfigureAwait(false);

            context.Params = parameters;
            try { return await next(context, cancellationToken).ConfigureAwait(false); }
            finally { context.Params = original; }
        });
    }
}
