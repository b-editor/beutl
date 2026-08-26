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
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

public sealed class AgentHostEndpointTests
{
    [AvaloniaTest]
    public async Task Normal_tracked_close_holds_live_session_suspension_until_completion_and_restores_on_abort()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommit = config.AutoCommitOnClose;
        config.AutoCommitOnClose = true;
        try
        {
            string location = Path.Combine(
                Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
                "agent-close-live-suspension");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640, 480, 30, 44100, "live-close", location))!;
            Assert.That(await TestShell.VersionControl.InitializeCurrentProjectAsync(
                project,
                _ => Task.FromResult<GitIdentity?>(
                    new GitIdentity("Beutl Test", "beutl@example.invalid"))), Is.True);
            TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().Single());
            Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var binding = new EditViewModelLiveBinding(editor);
            LiveEditingSession session = new LiveSessionSource().Attach(binding);
            TaskCompletionSource checkedDuringClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task ObserveClose(ProjectService.ProjectCloseContext _, CancellationToken __)
            {
                Assert.That(binding.IsAlive, Is.False);
                Assert.Throws<SessionUnavailableException>(() => session.Invoke(() => { }));
                checkedDuringClose.SetResult();
                await Task.CompletedTask;
            }

            TestShell.Project.Closing += ObserveClose;
            try
            {
                await TestShell.Project.CloseProject();
                await checkedDuringClose.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(binding.IsAlive, Is.False);

                var reopenedServiceReady = new TaskCompletionSource<IProjectVersionControlService>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable servicePublication =
                    TestShell.Editor.ProjectVersionControlService.Subscribe(service =>
                    {
                        if (service?.Repository is not null)
                        {
                            reopenedServiceReady.TrySetResult(service);
                        }
                    });
                await TestShell.Project.OpenProject(project.Uri!.LocalPath);
                IProjectVersionControlService reopenedService =
                    await reopenedServiceReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(TestShell.VersionControl.CurrentService, Is.SameAs(reopenedService));
                await reopenedService.GetStatusAsync(CancellationToken.None);
                Project reopened = TestShell.Project.CurrentProject.Value!;
                TestShell.Editor.ActivateTabItem(reopened.Items.OfType<Scene>().Single());
                Beutl.Testing.Headless.HeadlessTestHelpers.Settle();
                editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
                binding = new EditViewModelLiveBinding(editor);
                session = new LiveSessionSource().Attach(binding);
                async Task AbortClose(ProjectService.ProjectCloseContext _, CancellationToken __)
                {
                    Assert.That(binding.IsAlive, Is.False);
                    Assert.Throws<SessionUnavailableException>(() => session.Invoke(() => { }));
                    throw new ProjectCloseAbortedException("test abort");
                }

                TestShell.Project.ClosingPreparing += AbortClose;
                try
                {
                    try
                    {
                        await TestShell.Project.CloseProject();
                        Assert.Fail("The close should have been aborted.");
                    }
                    catch (ProjectCloseAbortedException)
                    {
                    }
                }
                finally
                {
                    TestShell.Project.ClosingPreparing -= AbortClose;
                }

                Assert.That(binding.IsAlive, Is.True);
                int invocations = 0;
                session.Invoke(() => invocations++);
                Assert.That(invocations, Is.EqualTo(1));
            }
            finally
            {
                TestShell.Project.Closing -= ObserveClose;
            }
        }
        finally
        {
            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommit;
        }
    }

    [AvaloniaTest]
    public async Task Live_session_rejects_edits_while_the_editors_are_suspended()
    {
        await TestReset.ResetShellAsync();
        string location = Path.Combine(
            Beutl.Testing.Headless.BeutlHomeIsolation.CurrentHome!,
            "agent-live-suspension");
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
        LiveEditingSession session = new LiveSessionSource().Attach(binding);

        Assert.That(session.ProbeIsAlive(), Is.True);

        using (IDisposable suspension = TestShell.Editor.SuspendEditors())
        {
            // A transition holds this from before its pre-transition save until the project closes,
            // so an edit accepted here would reach only the in-memory scene and be lost.
            Assert.Multiple(() =>
            {
                Assert.That(binding.IsAlive, Is.False);
                Assert.Throws<SessionUnavailableException>(() => session.Invoke(() => { }));
            });
        }

        int invocations = 0;
        session.Invoke(() => invocations++);

        Assert.Multiple(() =>
        {
            Assert.That(binding.IsAlive, Is.True);
            Assert.That(invocations, Is.EqualTo(1));
        });

        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public async Task Live_output_operation_provider_delegates_to_editor_service_exclusion()
    {
        await TestReset.ResetShellAsync();
        var editorService = new EditorService(new ExtensionProvider());
        IOutputOperationLeaseProvider provider = editorService;

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
    public async Task Shutdown_drain_rejects_authenticated_requests_and_reopens_after_abort()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "shutdown-gate-token");

        try
        {
            await endpoint.StartAsync();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", endpoint.Token);

            await using AgentHostEndpoint.AgentHostShutdownScope scope =
                await endpoint.BeginShutdownDrainAsync();
            using HttpResponseMessage paused = await client.GetAsync(endpoint.EndpointUri);
            Assert.That(paused.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

            await scope.DisposeAsync();
            using HttpResponseMessage resumed = await client.GetAsync(endpoint.EndpointUri);
            Assert.That(resumed.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable));
        }
        finally
        {
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task Shutdown_drain_waits_for_two_admitted_requests_and_rejects_new_work()
    {
        await TestReset.ResetShellAsync();
        var entered = new TaskCompletionSource[2]
        {
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var releases = new TaskCompletionSource[2]
        {
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        int index = 0;
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "shutdown-two-request-token",
            static _ => Task.CompletedTask,
            admissionProbe: async () =>
            {
                int current = Interlocked.Increment(ref index) - 1;
                entered[current].TrySetResult();
                await releases[current].Task;
            });

        try
        {
            await endpoint.StartAsync();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", endpoint.Token);
            Task<HttpResponseMessage> first = client.GetAsync(endpoint.EndpointUri);
            await entered[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<HttpResponseMessage> second = client.GetAsync(endpoint.EndpointUri);
            await entered[1].Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<AgentHostEndpoint.AgentHostShutdownScope> prepare = endpoint.BeginShutdownDrainAsync();
            Assert.That(prepare.IsCompleted, Is.False);
            releases[0].TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(prepare.IsCompleted, Is.False);
            releases[1].TrySetResult();
            await using AgentHostEndpoint.AgentHostShutdownScope scope =
                await prepare.WaitAsync(TimeSpan.FromSeconds(5));

            using HttpResponseMessage paused = await client.GetAsync(endpoint.EndpointUri);
            Assert.That(paused.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            await scope.DisposeAsync();
            await Task.WhenAll(first, second);
        }
        finally
        {
            releases[0].TrySetResult();
            releases[1].TrySetResult();
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task Concurrent_shutdown_preparation_is_rejected_without_reopening_the_owner()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "shutdown-concurrent-token");
        try
        {
            await endpoint.StartAsync();
            Task<AgentHostEndpoint.AgentHostShutdownScope> owner = endpoint.BeginShutdownDrainAsync();
            Assert.That(
                () => endpoint.BeginShutdownDrainAsync(),
                Throws.TypeOf<InvalidOperationException>());
            await using AgentHostEndpoint.AgentHostShutdownScope scope =
                await owner.WaitAsync(TimeSpan.FromSeconds(5));
            await scope.DisposeAsync();
        }
        finally
        {
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task Canceled_shutdown_preparation_reopens_admission_and_can_retry()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "shutdown-cancel-token");
        try
        {
            await endpoint.StartAsync();
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.That(
                async () => await endpoint.BeginShutdownDrainAsync(canceled.Token),
                Throws.InstanceOf<OperationCanceledException>());

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", endpoint.Token);
            using HttpResponseMessage resumed = await client.GetAsync(endpoint.EndpointUri);
            Assert.That(resumed.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable));

            await using AgentHostEndpoint.AgentHostShutdownScope scope =
                await endpoint.BeginShutdownDrainAsync();
            await scope.DisposeAsync();
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
