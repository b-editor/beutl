using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

public sealed class AgentHostEndpointTests
{
    [AvaloniaTest]
    public async Task Live_session_rejects_mutations_while_the_editor_is_disabled()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "agent-live-disabled-editor");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "live",
                location))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var binding = new EditViewModelLiveBinding(editor);
            var source = new LiveSessionSource();
            LiveEditingSession session = source.Attach(binding);
            int invocations = 0;

            Assert.That(session.ProbeIsAlive(), Is.True);

            editor.IsEnabled.Value = false;
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(binding.IsAlive, Is.False);
                    Assert.That(source.CurrentSession, Is.Null);
                    Assert.Throws<SessionUnavailableException>(
                        () => session.Invoke(() => invocations++));
                    Assert.That(invocations, Is.Zero);
                });
            }
            finally
            {
                editor.IsEnabled.Value = true;
            }

            session.Invoke(() => invocations++);

            Assert.Multiple(() =>
            {
                Assert.That(binding.IsAlive, Is.True);
                Assert.That(source.CurrentSession, Is.SameAs(session));
                Assert.That(invocations, Is.EqualTo(1));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Add_scene_rejects_a_disabled_live_editor_without_mutating_the_project()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "agent-add-scene-disabled-editor");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "live",
                location))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var liveSessions = new LiveSessionSource();
            LiveEditingSession session = liveSessions.Attach(new EditViewModelLiveBinding(editor));
            var sessions = new AgentSessionManager();
            sessions.UseSource(liveSessions);
            var gateway = new EditorProjectSessionGateway(
                TestShell.Project,
                TestShell.Editor,
                liveSessions,
                sessions,
                new WorkspaceGuard(Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!));
            Dictionary<string, string> filesBefore = SnapshotFiles(location);

            editor.IsEnabled.Value = false;
            try
            {
                SessionUnavailableException? rejection = null;
                try
                {
                    await gateway.AddSceneAsync(session, new SceneCreateOptions(
                        320,
                        180,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        "blocked-scene"));
                    Assert.Fail("Expected a SessionUnavailableException.");
                }
                catch (SessionUnavailableException ex)
                {
                    rejection = ex;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(rejection, Is.Not.Null);
                    Assert.That(project.Items.OfType<Scene>().Count(), Is.EqualTo(1));
                    Assert.That(SnapshotFiles(location), Is.EqualTo(filesBefore));
                });
            }
            finally
            {
                editor.IsEnabled.Value = true;
            }

            ProjectSceneResult added = await gateway.AddSceneAsync(session, new SceneCreateOptions(
                320,
                180,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                "added-scene"));

            Assert.Multiple(() =>
            {
                Assert.That(project.Items.OfType<Scene>().Count(), Is.EqualTo(2));
                Assert.That(project.Items, Does.Contain(added.Scene));
                Assert.That(File.Exists(added.Scene.Uri!.LocalPath), Is.True);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static Dictionary<string, string> SnapshotFiles(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => Convert.ToBase64String(File.ReadAllBytes(path)),
                StringComparer.Ordinal);
    }

    [AvaloniaTest]
    public async Task Live_session_reports_unavailable_as_soon_as_editor_disposal_starts()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "agent-live-disposed-editor-state");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "live",
                location))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var binding = new EditViewModelLiveBinding(editor);
            var source = new LiveSessionSource();
            LiveEditingSession session = source.Attach(binding);

            FieldInfo disposed = typeof(EditViewModel).GetField(
                "_disposed",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            disposed.SetValue(editor, true);

            Assert.Multiple(() =>
            {
                Assert.That(editor.Scene, Is.Not.Null);
                Assert.That(binding.IsAlive, Is.False);
                Assert.That(source.CurrentSession, Is.Null);
                Assert.Throws<SessionUnavailableException>(() => session.Invoke(static () => { }));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Live_binding_reports_unavailable_when_its_scene_is_cleared()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "agent-live-cleared-scene");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "live",
                location))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var binding = new EditViewModelLiveBinding(editor);
            FieldInfo sceneField = typeof(EditViewModel).GetField(
                "<Scene>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            sceneField.SetValue(editor, null);
            try
            {
                Assert.That(binding.IsAlive, Is.False);
            }
            finally
            {
                sceneField.SetValue(editor, scene);
            }
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
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
