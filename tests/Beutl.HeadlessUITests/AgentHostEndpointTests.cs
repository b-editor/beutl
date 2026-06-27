using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Api.Services;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

public sealed class AgentHostEndpointTests
{
    [AvaloniaTest]
    public async Task Endpoint_binds_loopback_issues_token_and_stops_cleanly()
    {
        await TestReset.ResetShellAsync();
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
                Assert.That(endpoint.EndpointUri.Port, Is.GreaterThan(0));
                Assert.That(endpoint.Token, Has.Length.EqualTo(32));
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
        });
    }
}
