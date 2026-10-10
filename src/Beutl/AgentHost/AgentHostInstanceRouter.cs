using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.AgentToolkit.Common;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed record AgentHostInstanceInfo(
    string InstanceId, int ProcessId, string? ProjectName, string? ProjectPath,
    string? SceneId, string? SceneName, string WorkspaceRoot);

public sealed record ListAgentHostInstancesResponse(
    string ConnectedInstanceId, IReadOnlyList<AgentHostInstanceInfo> Instances);

internal sealed class AgentHostInstanceRouter(
    AgentHostInstanceRegistry registry, ProjectService projects, EditorService editors,
    string token, Func<string> workspaceRoot, string? instanceId = null)
{
    internal const string InstanceHeader = "X-Beutl-Instance-Id";
    private static readonly TimeSpan s_connectionTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AgentHostInstanceAuthentication _authentication = new(token, instanceId ?? Guid.NewGuid().ToString("N"));

    public string InstanceId => _authentication.InstanceId;

    public AgentHostIdentityProof CreateIdentityProof(string challenge) => _authentication.CreateProof(challenge);

    public bool IsForwardToken(string provided) => _authentication.IsForwardToken(provided);

    public async Task<AgentHostInstanceInfo> GetInfoAsync(CancellationToken cancellationToken)
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Project? project = projects.CurrentProject.Value;
            var editor = editors.SelectedTabItem.Value?.Context.Value as EditViewModel;
            string? projectPath = project?.Uri is { IsFile: true } uri ? uri.LocalPath : null;
            string? projectName = string.IsNullOrWhiteSpace(project?.Name)
                ? Path.GetFileNameWithoutExtension(projectPath)
                : project.Name;
            return new AgentHostInstanceInfo(
                InstanceId, Environment.ProcessId, projectName, projectPath,
                editor?.Scene.Id.ToString(), editor?.Scene.Name, workspaceRoot());
        }, DispatcherPriority.Normal, cancellationToken);

    public async Task<ListAgentHostInstancesResponse> ListAsync(CancellationToken cancellationToken)
    {
        AgentHostInstanceInfo self = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        Task<AgentHostInstanceInfo?>[] tasks = registry.Read().Where(entry => entry.InstanceId != InstanceId)
            .Select(entry => ReadInfoAsync(entry, cancellationToken)).ToArray();
        AgentHostInstanceInfo?[] instances = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new ListAgentHostInstancesResponse(InstanceId, instances.OfType<AgentHostInstanceInfo>().Prepend(self)
            .OrderBy(info => info.ProcessId).ThenBy(info => info.InstanceId, StringComparer.Ordinal).ToArray());
    }

    private async Task<AgentHostInstanceInfo?> ReadInfoAsync(
        AgentHostInstanceRegistration registration, CancellationToken cancellationToken)
    {
        if (registration.InstanceId == InstanceId)
            return await GetInfoAsync(cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(s_connectionTimeout);
        using HttpClient http = CreateHttpClient(registration.InstanceId);
        try
        {
            if (!await AuthenticatePeerAsync(registration, http, timeout.Token).ConfigureAwait(false))
                return null;
            var info = await http.GetFromJsonAsync<AgentHostInstanceInfo>(
                new Uri(registration.EndpointUri, "/agent-host"), s_jsonOptions, timeout.Token).ConfigureAwait(false);
            if (info?.InstanceId == registration.InstanceId)
                return info;
            registry.RemoveIfUnchanged(registration);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async ValueTask<CallToolResult> ForwardAsync(
        string instanceId, CallToolRequestParams parameters, McpServer server, CancellationToken cancellationToken)
    {
        AgentHostInstanceRegistration? registration = registry.Read()
            .FirstOrDefault(entry => entry.InstanceId == instanceId);
        if (registration is null)
            return Unavailable(instanceId);

        using HttpClient http = CreateHttpClient(instanceId);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = registration.EndpointUri,
            TransportMode = HttpTransportMode.StreamableHttp
        }, http);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(s_connectionTimeout);
            if (!await AuthenticatePeerAsync(registration, http, timeout.Token).ConfigureAwait(false))
                return Unavailable(instanceId);
            await using McpClient client = await McpClient.CreateAsync(
                transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            await using IAsyncDisposable progress = client.RegisterNotificationHandler(
                NotificationMethods.ProgressNotification,
                (notification, ct) => new ValueTask(server.SendNotificationAsync(
                    notification.Method, notification.Params, cancellationToken: ct)));
            // Never retry a failed call: an edit may already have reached the target process.
            return await client.CallToolAsync(parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or McpException or IOException
                                   || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Unavailable(instanceId);
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private HttpClient CreateHttpClient(string instanceId)
    {
        // A profile registration must never send the bearer token through a proxy or redirect.
        var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        http.DefaultRequestHeaders.Add(InstanceHeader, instanceId);
        return http;
    }

    private async Task<bool> AuthenticatePeerAsync(
        AgentHostInstanceRegistration registration, HttpClient http, CancellationToken cancellationToken)
    {
        string challenge = AgentHostInstanceAuthentication.CreateChallenge();
        try
        {
            var proof = await http.GetFromJsonAsync<AgentHostIdentityProof>(
                new Uri(registration.EndpointUri, "/agent-host/identity?challenge=" + challenge),
                s_jsonOptions, cancellationToken).ConfigureAwait(false);
            if (!_authentication.VerifyProof(registration.InstanceId, challenge, proof))
            {
                // A different token does not prove that a host is dead. Prune only an identity
                // mismatch; leave transient failures and same-ID authentication failures intact.
                if (proof?.InstanceId != registration.InstanceId)
                    registry.RemoveIfUnchanged(registration);
                return false;
            }

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _authentication.CreateForwardToken(registration.InstanceId));
            return true;
        }
        catch (HttpRequestException ex) when (IsMissingIdentityEndpoint(ex))
        {
            registry.RemoveIfUnchanged(registration);
            return false;
        }
        catch (JsonException)
        {
            registry.RemoveIfUnchanged(registration);
            return false;
        }
    }

    private static bool IsMissingIdentityEndpoint(HttpRequestException exception)
    {
        if (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Conflict)
            return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
                return true;
        }
        return false;
    }

    private static CallToolResult Unavailable(string instanceId)
        => Error("instance_unavailable", "The requested Beutl instance is unavailable.", instanceId,
            "Call list_instances to find a running instance. If a previous edit lost its response, read back the target state before retrying.");

    private static CallToolResult Error(string code, string message, string target, string hint)
        => new()
        {
            Content = [new TextContentBlock
            {
                Text = JsonSerializer.Serialize(ToolResult<object?>.Failure(code, message, target, hint), s_jsonOptions)
            }]
        };

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
                    ["description"] = "Target Beutl instance ID from list_instances. Pass the same ID on every call, including attach, read, edit, history, render and job polling. Omit to operate on the connected instance."
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
                return Error(ErrorCode.ValidationRejected, "Pass an instanceId returned by list_instances.",
                    "instanceId", "Call list_instances without arguments to discover running Beutl instances.");

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
