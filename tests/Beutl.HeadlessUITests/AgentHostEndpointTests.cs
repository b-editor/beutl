using System.Net;
using System.Net.Sockets;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Api.Services;
using Beutl.Services;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

public sealed class AgentHostEndpointTests
{
    [AvaloniaTest]
    public async Task Endpoint_binds_default_loopback_port_uses_fixed_token_and_stops_cleanly()
    {
        await TestReset.ResetShellAsync();
        if (!CanBindLoopbackPort(AgentHostEndpoint.DefaultPort))
        {
            Assert.Inconclusive($"Default port {AgentHostEndpoint.DefaultPort} is already in use.");
        }

        var endpoint = new AgentHostEndpoint(new EditorService(new ExtensionProvider()));

        try
        {
            await endpoint.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.IsRunning, Is.True);
                Assert.That(endpoint.EndpointUri, Is.Not.Null);
                Assert.That(endpoint.EndpointUri!.Host, Is.EqualTo("127.0.0.1"));
                Assert.That(endpoint.EndpointUri.AbsolutePath, Is.EqualTo("/mcp"));
                Assert.That(endpoint.EndpointUri.Port, Is.EqualTo(AgentHostEndpoint.DefaultPort));
                Assert.That(endpoint.Token, Is.EqualTo(AgentHostEndpoint.DefaultToken));
            });

            using var client = new HttpClient();
            using HttpResponseMessage rejected = await client.GetAsync(endpoint.EndpointUri);
            Assert.That((int)rejected.StatusCode, Is.EqualTo(401));

            using HttpRequestMessage request = new(HttpMethod.Get, endpoint.EndpointUri);
            request.Headers.Add("X-Beutl-Agent-Token", endpoint.Token);
            using HttpResponseMessage accepted = await client.SendAsync(request);
            Assert.That((int)accepted.StatusCode, Is.Not.EqualTo(401));
        }
        finally
        {
            await endpoint.StopAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task Endpoint_increments_port_when_preferred_port_is_in_use()
    {
        await TestReset.ResetShellAsync();
        using TcpListener occupiedPort = ReserveLoopbackPortWithAvailableSuccessor();
        int preferredPort = ((IPEndPoint)occupiedPort.LocalEndpoint).Port;
        var endpoint = new AgentHostEndpoint(
            new EditorService(new ExtensionProvider()),
            preferredPort,
            AgentHostEndpoint.DefaultToken);

        try
        {
            await endpoint.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.EndpointUri, Is.Not.Null);
                Assert.That(endpoint.EndpointUri!.Host, Is.EqualTo("127.0.0.1"));
                Assert.That(endpoint.EndpointUri.Port, Is.EqualTo(preferredPort + 1));
            });
        }
        finally
        {
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task RequestStop_marks_endpoint_stopped_without_awaiting_host_shutdown()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(new EditorService(new ExtensionProvider()));

        await endpoint.StartAsync();
        endpoint.RequestStop();

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        });

        await endpoint.StopAsync();
    }

    [AvaloniaTest]
    public async Task Endpoint_tools_list_includes_live_host_and_design_tools()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token");

        try
        {
            await endpoint.StartAsync();

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint.EndpointUri!,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["X-Beutl-Agent-Token"] = endpoint.Token
                }
            });
            await using McpClient client = await McpClient.CreateAsync(transport);

            string[] toolNames = [.. (await client.ListToolsAsync()).Select(tool => tool.Name)];

            Assert.Multiple(() =>
            {
                Assert.That(toolNames, Does.Contain("derive_palette"));
                Assert.That(toolNames, Does.Contain("get_background_grammar"));
                Assert.That(toolNames, Does.Contain("attach_active_editor"));
                Assert.That(toolNames, Does.Contain("apply_edit"));
                Assert.That(toolNames, Does.Contain("render_still"));
            });
        }
        finally
        {
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task Endpoint_constructs_render_tools_for_tool_calls()
    {
        // tools/list alone never constructs tool classes, so only a call catches a DI registration missing from this host.
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token");

        try
        {
            await endpoint.StartAsync();

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint.EndpointUri!,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["X-Beutl-Agent-Token"] = endpoint.Token
                }
            });
            await using McpClient client = await McpClient.CreateAsync(transport);

            CallToolResult result = await client.CallToolAsync(
                "analyze_audio_rhythm",
                new Dictionary<string, object?> { ["path"] = "does-not-exist.wav" });

            string text = string.Join(
                "\n",
                result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            Assert.That(text, Does.Contain("media_not_found"));
        }
        finally
        {
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task AttachActiveEditor_without_open_editor_returns_typed_error()
    {
        await TestReset.ResetShellAsync();
        var editorService = new EditorService(new ExtensionProvider());
        var liveSessions = new LiveSessionSource();
        var sessions = new AgentSessionManager();
        var tools = new AgentHostTools(editorService, liveSessions, sessions);

        ToolResult<AttachActiveEditorResponse> result = tools.AttachActiveEditor();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.NoActiveEditorSession));
            Assert.That(result.Error?.Hint, Does.Contain("attach_active_editor"));
        });
    }

    private static TcpListener ReserveLoopbackPortWithAvailableSuccessor()
    {
        for (int i = 0; i < 50; i++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            if (port < IPEndPoint.MaxPort && CanBindLoopbackPort(port + 1))
            {
                return listener;
            }

            listener.Stop();
        }

        Assert.Inconclusive("Could not reserve a loopback port with an available successor.");
        throw new InvalidOperationException();
    }

    private static int GetAvailableLoopbackPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool CanBindLoopbackPort(int port)
    {
        try
        {
            using TcpListener listener = new(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            return false;
        }
    }
}
