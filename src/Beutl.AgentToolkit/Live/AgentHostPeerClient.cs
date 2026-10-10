using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Live;

// Talks to the Beutl processes of one profile: proves a process's identity with the shared live
// MCP token before any credential is sent to its port, then forwards MCP calls over its loopback
// endpoint. Shared by the in-process instance router and by the stdio live MCP broker.
public sealed class AgentHostPeerClient(AgentHostInstanceRegistry registry, AgentHostInstanceAuthentication authentication)
{
    public const string InstanceHeader = "X-Beutl-Instance-Id";
    private static readonly TimeSpan s_connectionTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public AgentHostInstanceRegistry Registry => registry;

    public AgentHostInstanceRegistration? Find(string instanceId)
        => registry.Read().FirstOrDefault(entry => entry.InstanceId == instanceId);

    public async Task<AgentHostInstanceInfo?> ReadInfoAsync(
        AgentHostInstanceRegistration registration, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(s_connectionTimeout);
        using HttpClient http = CreateHttpClient(registration.InstanceId);
        try
        {
            if (!await AuthenticateAsync(registration, http, timeout.Token).ConfigureAwait(false))
                return null;
            var info = await http.GetFromJsonAsync<AgentHostInstanceInfo>(
                new Uri(registration.EndpointUri, "/agent-host"), s_jsonOptions, timeout.Token).ConfigureAwait(false);
            if (info?.InstanceId == registration.InstanceId)
                return info;
            registry.RemoveIfUnchanged(registration);
            return null;
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return null;
        }
    }

    // The protocol tools of a running instance, or null when it is gone or not ours.
    public async Task<IList<Tool>?> ListToolsAsync(
        AgentHostInstanceRegistration registration, CancellationToken cancellationToken)
    {
        using HttpClient http = CreateHttpClient(registration.InstanceId);
        HttpClientTransport transport = CreateTransport(registration, http);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(s_connectionTimeout);
            if (!await AuthenticateAsync(registration, http, timeout.Token).ConfigureAwait(false))
                return null;
            await using McpClient client = await McpClient.CreateAsync(
                transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
            return tools.Select(tool => tool.ProtocolTool).ToList();
        }
        catch (Exception ex) when (ex is McpException or IOException || IsTransportFailure(ex, cancellationToken))
        {
            return null;
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<CallToolResult> ForwardAsync(
        string instanceId, CallToolRequestParams parameters, McpServer server, CancellationToken cancellationToken)
    {
        AgentHostInstanceRegistration? registration = Find(instanceId);
        return registration is null
            ? LiveToolResults.InstanceUnavailable(instanceId)
            : await ForwardAsync(registration, parameters, server, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CallToolResult> ForwardAsync(
        AgentHostInstanceRegistration registration, CallToolRequestParams parameters, McpServer server,
        CancellationToken cancellationToken)
    {
        string instanceId = registration.InstanceId;
        using HttpClient http = CreateHttpClient(instanceId);
        HttpClientTransport transport = CreateTransport(registration, http);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(s_connectionTimeout);
            if (!await AuthenticateAsync(registration, http, timeout.Token).ConfigureAwait(false))
                return LiveToolResults.InstanceUnavailable(instanceId);
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
            return LiveToolResults.InstanceUnavailable(instanceId);
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static HttpClientTransport CreateTransport(AgentHostInstanceRegistration registration, HttpClient http)
        => new(new HttpClientTransportOptions
        {
            Endpoint = registration.EndpointUri,
            TransportMode = HttpTransportMode.StreamableHttp
        }, http);

    private static HttpClient CreateHttpClient(string instanceId)
    {
        // A profile registration must never send a credential through a proxy or redirect.
        var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        http.DefaultRequestHeaders.Add(InstanceHeader, instanceId);
        return http;
    }

    private async Task<bool> AuthenticateAsync(
        AgentHostInstanceRegistration registration, HttpClient http, CancellationToken cancellationToken)
    {
        string challenge = AgentHostInstanceAuthentication.CreateChallenge();
        try
        {
            var proof = await http.GetFromJsonAsync<AgentHostIdentityProof>(
                new Uri(registration.EndpointUri, "/agent-host/identity?challenge=" + challenge),
                s_jsonOptions, cancellationToken).ConfigureAwait(false);
            if (!authentication.VerifyProof(registration.InstanceId, challenge, proof))
            {
                // A different token does not prove that a host is dead. Prune only an identity
                // mismatch; leave transient failures and same-ID authentication failures intact.
                if (proof?.InstanceId != registration.InstanceId)
                    registry.RemoveIfUnchanged(registration);
                return false;
            }

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", authentication.CreateForwardToken(registration.InstanceId));
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

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException or JsonException
           || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
