using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Live;
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
            var gateway = new EditorProjectSessionGateway(TestShell.Project, TestShell.Editor);
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
    public async Task StopAsync_detaches_a_published_host_while_startup_completion_is_pending()
    {
        await TestReset.ResetShellAsync();
        await using var endpoint = new AgentHostEndpoint(
            new ProjectService(), new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(), "test-token");
        await endpoint.StartAsync();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(AgentHostEndpoint).GetField("_startupTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(endpoint, completion.Task);
        try
        {
            _ = endpoint.StopAsync();
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        }
        finally
        {
            completion.TrySetResult();
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task Direct_startup_callback_cannot_hold_the_lifecycle_lock_past_stop_timeout()
    {
        await TestReset.ResetShellAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var endpoint = new AgentHostEndpoint(
            new ProjectService(), new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(), "test-token", _ =>
            {
                entered.TrySetResult();
                release.Wait();
                return Task.CompletedTask;
            });
        Task startup = Task.Run(() => endpoint.StartAsync());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() => endpoint.StopAsync()).WaitAsync(TimeSpan.FromSeconds(4));
            Assert.That(endpoint.IsRunning, Is.False);
        }
        finally
        {
            release.Set();
            try { await startup.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
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
    public async Task Independent_configurations_share_the_persisted_profile_token()
    {
        await TestReset.ResetShellAsync();
        string directory = Directory.CreateTempSubdirectory("endpoint-token-").FullName;
        var firstConfig = new AiAgentConfig();
        var secondConfig = new AiAgentConfig { LiveMcpToken = "old-snapshot-token" };
        try
        {
            await using var first = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), firstConfig, directory);
            await using var second = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), secondConfig, directory);
            string firstInstanceId = first.InstanceId;
            await first.StartAsync();
            await second.StartAsync();
            Assert.Multiple(() =>
            {
                Assert.That(first.InstanceId, Is.EqualTo(firstInstanceId));
                Assert.That(first.Token, Does.Match("^[0-9A-F]{32}$"));
                Assert.That(firstConfig.LiveMcpToken, Is.Empty);
                Assert.That(secondConfig.LiveMcpToken, Is.Empty);
                Assert.That(second.Token, Is.EqualTo(first.Token));
                Assert.That(LiveMcpTokenStore.GetOrCreate(directory), Is.EqualTo(first.Token));
            });
            using var http = new HttpClient();
            using (HttpResponseMessage rejected = await http.GetAsync(new Uri(first.EndpointUri!, "/agent-host")))
                Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
            foreach (AgentHostEndpoint endpoint in new[] { first, second })
            {
                AgentHostInstanceInfo? info = await http.GetFromJsonAsync<AgentHostInstanceInfo>(new Uri(endpoint.EndpointUri!, "/agent-host"));
                Assert.That(info?.InstanceId, Is.EqualTo(endpoint.InstanceId));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTest]
    [TestCase("live-mcp-token.json")]
    [TestCase("settings.json")]
    public async Task Invalid_credentials_fail_background_start_without_blocking_editor_construction(string fileName)
    {
        await TestReset.ResetShellAsync();
        string directory = Directory.CreateTempSubdirectory("endpoint-invalid-token-").FullName;
        string invalidPath = Path.Combine(directory, fileName);
        File.WriteAllText(invalidPath, "{");
        var config = new AiAgentConfig { LiveMcpToken = "pending-migration-token" };
        try
        {
            await using var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), config, directory);
            Assert.That(File.Exists(Path.Combine(directory, "live-mcp-token.lock")), Is.False,
                "Editor construction must not acquire the token store or wait for its lock.");
            endpoint.StartInBackground();
            await Assert.ThrowsAsync<InvalidDataException>(async () => await endpoint.StartAsync());
            Assert.Multiple(() =>
            {
                Assert.That(endpoint.IsRunning, Is.False);
                Assert.That(endpoint.EndpointUri, Is.Null);
                Assert.That(endpoint.Token, Is.Empty);
                Assert.That(config.LiveMcpToken, Is.EqualTo("pending-migration-token"));
                Assert.That(File.ReadAllText(invalidPath), Is.EqualTo("{"));
            });
            if (fileName == "settings.json")
                Assert.That(File.Exists(Path.Combine(directory, LiveMcpTokenStore.FileName)), Is.False);
        }
        finally { Directory.Delete(directory, true); }
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
    public async Task ConcurrentStartCallersCancelOnlyTheirOwnWaits()
    {
        await TestReset.ResetShellAsync();
        var startupEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            async token =>
            {
                startupEntered.TrySetResult();
                await releaseStartup.Task.WaitAsync(token);
            });
        using var firstCancellation = new CancellationTokenSource();
        using var laterCancellation = new CancellationTokenSource();
        Task first = endpoint.StartAsync(firstCancellation.Token);
        await startupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task shared = endpoint.StartAsync();
        Task later = endpoint.StartAsync(laterCancellation.Token);

        firstCancellation.Cancel();
        laterCancellation.Cancel();
        await AssertCanceledAsync(first);
        await AssertCanceledAsync(later);
        Assert.That(shared.IsCompleted, Is.False);

        try
        {
            releaseStartup.TrySetResult();
            await shared.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(endpoint.IsRunning, Is.True);
        }
        finally
        {
            releaseStartup.TrySetResult();
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task StopAsync_before_start_keeps_the_endpoint_stopped()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()));

        // A stop requested before startup must win: StartAsync must not bring the host up afterward.
        _ = endpoint.StopAsync();
        await endpoint.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        });

        await endpoint.StopAsync();
    }

    [AvaloniaTest]
    public async Task StopAsync_marks_endpoint_stopped_without_awaiting_host_shutdown()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()));

        await endpoint.StartAsync();
        _ = endpoint.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        });

        await endpoint.StopAsync();
    }

    [AvaloniaTest]
    public async Task StopAsync_joins_background_startup_and_leaves_no_published_endpoint()
    {
        await TestReset.ResetShellAsync();
        var startupEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            async _ =>
            {
                startupEntered.TrySetResult();
                await releaseStartup.Task;
            });

        endpoint.StartInBackground();
        await startupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stop = endpoint.StopAsync();
        Assert.That(stop.IsCompleted, Is.False);
        releaseStartup.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task StopAsync_bounds_a_startup_path_that_does_not_observe_cancellation()
    {
        await TestReset.ResetShellAsync();
        var startupEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            async _ =>
            {
                startupEntered.TrySetResult();
                await releaseStartup.Task;
            });
        Task startup = endpoint.StartAsync();
        await startupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await endpoint.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.False);
            Assert.That(endpoint.EndpointUri, Is.Null);
        });

        releaseStartup.TrySetResult();
        await AssertCanceledAsync(startup);
    }

    [AvaloniaTest]
    public async Task Endpoint_tools_list_exposes_editing_and_measurement_without_quality_gates()
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
                Assert.That(toolNames, Does.Not.Contain("evaluate_edit_quality"));
                Assert.That(toolNames, Does.Contain("measure_frame_differences"));
                Assert.That(toolNames, Does.Contain("list_scenes"));
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
                new Dictionary<string, object?>
                {
                    ["path"] = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.wav")
                });

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
    public async Task ListScenes_without_open_editor_returns_an_empty_list()
    {
        await TestReset.ResetShellAsync();
        var editorService = new EditorService(new ExtensionProvider());
        var projects = new ProjectService();
        var gateway = new EditorProjectSessionGateway(projects, editorService);
        var tools = new AgentHostTools(projects, editorService, gateway);

        ToolResult<ListScenesResponse> result = tools.ListScenes();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Scenes, Is.Empty);
            Assert.That(result.Value.ActiveSceneId, Is.Null);
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

    private static async Task AssertCanceledAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { return; }
        Assert.Fail("Expected startup cancellation.");
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
