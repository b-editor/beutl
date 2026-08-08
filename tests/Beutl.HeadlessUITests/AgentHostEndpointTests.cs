using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Services;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

public sealed class AgentHostEndpointTests
{
    [AvaloniaTest]
    public async Task Live_output_operation_provider_delegates_to_editor_service_exclusion()
    {
        await TestReset.ResetShellAsync();
        var editorService = new EditorService(new ExtensionProvider());
        var provider = new EditorOutputOperationLeaseProvider(editorService);

        using (IDisposable worktreeMutation = editorService.TryBeginWorktreeMutation()!)
        {
            Assert.That(provider.TryBeginOutputOperation(), Is.Null);
        }

        using (IDisposable outputOperation = provider.TryBeginOutputOperation()!)
        {
            Assert.That(editorService.TryBeginWorktreeMutation(), Is.Null);
        }

        using IDisposable? mutationAfterOutput = editorService.TryBeginWorktreeMutation();
        Assert.That(mutationAfterOutput, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task Endpoint_binds_default_loopback_port_uses_fixed_token_and_stops_cleanly()
    {
        await TestReset.ResetShellAsync();
        if (!CanBindLoopbackPort(AgentHostEndpoint.DefaultPort))
        {
            Assert.Inconclusive($"Default port {AgentHostEndpoint.DefaultPort} is already in use.");
        }

        var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()));

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
                Assert.That(endpoint.Token, Has.Length.EqualTo(32));
            });

            using var client = new HttpClient();
            using HttpResponseMessage rejected = await client.GetAsync(endpoint.EndpointUri);
            Assert.That((int)rejected.StatusCode, Is.EqualTo(401));

            // The query-token form was removed; only the Authorization header authenticates.
            using HttpResponseMessage queryRejected = await client.GetAsync(
                new Uri(endpoint.EndpointUri + "?token=" + endpoint.Token));
            Assert.That((int)queryRejected.StatusCode, Is.EqualTo(401));

            using HttpRequestMessage request = new(HttpMethod.Get, endpoint.EndpointUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
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
    public async Task Default_constructor_generates_and_persists_a_random_token()
    {
        await TestReset.ResetShellAsync();
        var config = new AiAgentConfig();

        var first = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), config);
        var second = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), config);

        Assert.Multiple(() =>
        {
            Assert.That(first.Token, Does.Match("^[0-9A-F]{32}$"));
            Assert.That(config.LiveMcpToken, Is.EqualTo(first.Token));
            Assert.That(second.Token, Is.EqualTo(first.Token));
        });
    }

    [AvaloniaTest]
    public async Task Resolve_workspace_root_uses_settings_default_when_config_and_environment_are_empty()
    {
        await TestReset.ResetShellAsync();
        string? previous = Environment.GetEnvironmentVariable("BEUTL_WORKSPACE");
        try
        {
            Environment.SetEnvironmentVariable("BEUTL_WORKSPACE", null);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string expected = string.IsNullOrWhiteSpace(documents)
                ? Directory.GetCurrentDirectory()
                : documents;

            string resolved = AgentHostEndpoint.ResolveWorkspaceRoot(new AiAgentConfig());

            Assert.That(resolved, Is.EqualTo(expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BEUTL_WORKSPACE", previous);
        }
    }

    [AvaloniaTest]
    public async Task Endpoint_increments_port_when_preferred_port_is_in_use()
    {
        await TestReset.ResetShellAsync();
        using TcpListener occupiedPort = ReserveLoopbackPortWithAvailableSuccessor();
        int preferredPort = ((IPEndPoint)occupiedPort.LocalEndpoint).Port;
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            preferredPort,
            "test-token");

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
    public async Task RequestStop_before_start_keeps_the_endpoint_stopped()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()));

        // A stop requested before startup must win: StartAsync must not bring the host up afterward.
        endpoint.RequestStop();
        await endpoint.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        });

        await endpoint.StopAsync();
    }

    [AvaloniaTest]
    public async Task StartAsync_can_retry_after_a_cancelled_start_attempt()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            Assert.That(
                async () => await endpoint.StartAsync(cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());

            await endpoint.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.IsRunning, Is.True);
                Assert.That(endpoint.EndpointUri, Is.Not.Null);
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
        var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()));

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
    public async Task DisposeAsync_waits_for_the_stop_started_by_RequestStop()
    {
        await TestReset.ResetShellAsync();
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            async _ =>
            {
                stopEntered.TrySetResult();
                await releaseStop.Task.ConfigureAwait(false);
            });

        try
        {
            await endpoint.StartAsync();
            endpoint.RequestStop();
            await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task disposalTask = endpoint.DisposeAsync().AsTask();
            Task repeatedDisposalTask = endpoint.DisposeAsync().AsTask();

            Assert.Multiple(() =>
            {
                Assert.That(disposalTask.IsCompleted, Is.False);
                Assert.That(repeatedDisposalTask.IsCompleted, Is.False);
            });

            releaseStop.TrySetResult();
            await Task.WhenAll(disposalTask, repeatedDisposalTask).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseStop.TrySetResult();
            await endpoint.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task RequestStop_then_DisposeAsync_propagates_the_retained_stop_failure()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            static _ => Task.FromException(
                new InvalidOperationException("Expected stop failure.")));

        try
        {
            await endpoint.StartAsync();
            endpoint.RequestStop();

            InvalidOperationException? first = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await endpoint.DisposeAsync());
            InvalidOperationException? second = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await endpoint.DisposeAsync());

            Assert.Multiple(() =>
            {
                Assert.That(first?.Message, Is.EqualTo("Expected stop failure."));
                Assert.That(second?.Message, Is.EqualTo("Expected stop failure."));
                Assert.That(endpoint.IsRunning, Is.False);
                Assert.That(endpoint.EndpointUri, Is.Null);
            });
        }
        finally
        {
            try
            {
                await endpoint.DisposeAsync();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    [AvaloniaTest]
    public async Task StopAsync_cancellation_does_not_cancel_the_retained_drain()
    {
        await TestReset.ResetShellAsync();
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            async cancellationToken =>
            {
                Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                stopEntered.TrySetResult();
                await releaseStop.Task.ConfigureAwait(false);
            });
        using var cancellation = new CancellationTokenSource();

        try
        {
            await endpoint.StartAsync();
            Task stopWait = endpoint.StopAsync(cancellation.Token);
            await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            Assert.That(
                async () => await stopWait,
                Throws.InstanceOf<OperationCanceledException>());

            Task disposal = endpoint.DisposeAsync().AsTask();
            Assert.That(disposal.IsCompleted, Is.False);

            releaseStop.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseStop.TrySetResult();
            await endpoint.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task RequestStop_during_start_hands_the_application_to_the_retained_drain()
    {
        await TestReset.ResetShellAsync();
        var applicationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            _ =>
            {
                stopEntered.TrySetResult();
                return Task.CompletedTask;
            },
            async cancellationToken =>
            {
                applicationStarted.TrySetResult();
                await releaseStartup.Task.WaitAsync(cancellationToken);
            });

        try
        {
            Task startup = endpoint.StartAsync();
            await applicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            endpoint.RequestStop();
            Task disposal = endpoint.DisposeAsync().AsTask();

            Assert.Multiple(() =>
            {
                Assert.That(startup.IsCompleted, Is.False);
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(endpoint.IsRunning, Is.False);
                Assert.That(endpoint.EndpointUri, Is.Null);
            });

            releaseStartup.TrySetResult();
            await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(startup, disposal).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.IsRunning, Is.False);
                Assert.That(endpoint.EndpointUri, Is.Null);
            });
        }
        finally
        {
            releaseStartup.TrySetResult();
            await endpoint.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Endpoint_tools_list_includes_live_host_and_design_tools()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
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
                    ["Authorization"] = "Bearer " + endpoint.Token
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
            new ProjectService(),
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
                    ["Authorization"] = "Bearer " + endpoint.Token
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
