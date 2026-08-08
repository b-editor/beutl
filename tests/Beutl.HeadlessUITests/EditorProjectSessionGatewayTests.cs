using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class EditorProjectSessionGatewayTests
{
    private sealed class TrackingLease : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }
    }

    private sealed class ShellTestScope : IAsyncDisposable
    {
        public static async Task<ShellTestScope> CreateAsync()
        {
            await TestReset.ResetShellAsync();
            return new ShellTestScope();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await TestReset.ResetShellAsync();
            }
            finally
            {
                // Avalonia.Headless creates a fresh TestApp for every [AvaloniaTest], so this
                // composition root is not reused by the next test.
                await TestShell.MainViewModel.DisposeAsync();
            }
        }
    }

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static (EditorProjectSessionGateway Gateway, AgentSessionManager Sessions) CreateGateway(
        string? workspaceRoot = null)
    {
        var sessions = new AgentSessionManager();
        var gateway = new EditorProjectSessionGateway(
            TestShell.Project,
            TestShell.Editor,
            new LiveSessionSource(),
            sessions,
            new WorkspaceGuard(workspaceRoot ?? BeutlHomeIsolation.CurrentHome!));
        return (gateway, sessions);
    }

    private static async Task<ReconcileException?> ExpectRejectionAsync(Func<ValueTask<ProjectSessionResult>> action)
    {
        try
        {
            await action();
            Assert.Fail("Expected a ReconcileException rejection.");
            return null;
        }
        catch (ReconcileException ex)
        {
            return ex;
        }
    }

    private static string CreateProjectFilesOnDisk(string name, TimeSpan duration)
    {
        string path = Path.Combine(NewWorkspace(name), $"{name}.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            path, 640, 360, 30, duration));
        ProjectOperations.Save(project);
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static int GetAvailableLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    [AvaloniaTest]
    public async Task OpenProject_opens_the_project_in_the_editor_and_serves_a_live_session()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-open", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, AgentSessionManager sessions) = CreateGateway();

        ProjectSessionResult result = await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.IsOpened.Value, Is.True);
            Assert.That(BeutlApplication.Current.Project!.Uri!.LocalPath, Is.EqualTo(projectFile));
            Assert.That(result.Session.Source, Is.EqualTo(EditingSessionSource.LiveEditor));
            Assert.That(sessions.CurrentSession, Is.Not.Null);
            Assert.That(sessions.CurrentSession!.SessionId, Is.EqualTo(result.Session.SessionId));
            Assert.That(TestShell.Editor.SelectedTabItem.Value?.Context.Value, Is.InstanceOf<EditViewModel>());
            var editViewModel = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            Assert.That(editViewModel.Scene, Is.SameAs(result.Session.Root));
        });
    }

    [AvaloniaTest]
    public async Task Main_view_model_dispose_starts_teardown_of_an_open_editor()
    {
        await TestReset.ResetShellAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-root-disposal", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        ProjectService projectService = TestShell.Project;
        MainViewModel mainViewModel = TestShell.MainViewModel;
        await gateway.OpenProjectAsync(projectFile);
        var editViewModel = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        try
        {
            mainViewModel.Dispose();
            await WaitUntilAsync(() =>
                TestShell.Editor.TabItems.Count == 0
                && TestShell.Editor.SelectedTabItem.Value is null
                && editViewModel.Player is null);

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Editor.TabItems, Is.Empty);
                Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.Null);
                Assert.That(editViewModel.Player, Is.Null);
            });
        }
        finally
        {
            projectService.CloseProjectImmediately();
            await mainViewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Shutdown_drains_background_render_before_closing_project_and_editor()
    {
        await TestReset.ResetShellAsync();
        var managerCreated = new TaskCompletionSource<RenderJobManager>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new TrackingLease();
        var mainViewModel = new MainViewModel((projectService, editorService) =>
            new AgentHostEndpoint(
                projectService,
                editorService,
                GetAvailableLoopbackPort(),
                "test-token",
                static _ => Task.CompletedTask,
                renderJobManagerFactory: () =>
                {
                    var manager = new RenderJobManager();
                    managerCreated.TrySetResult(manager);
                    return manager;
                }));
        string projectFile = CreateProjectFilesOnDisk(
            "gateway-agent-drain-order",
            TimeSpan.FromSeconds(4));
        int closingCalls = 0;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing = (_, _) =>
        {
            closingCalls++;
            return Task.CompletedTask;
        };
        Task? shutdown = null;

        try
        {
            await mainViewModel.ProjectService.OpenProject(projectFile);
            mainViewModel.ProjectService.Closing += closing;
            await mainViewModel.AgentHostEndpoint.StartAsync();
            Project project = mainViewModel.ProjectService.CurrentProject.Value!;
            RenderJobManager manager = await managerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            string jobId = manager.Enqueue("test", async token =>
            {
                jobStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                    await releaseCancellation.Task.ConfigureAwait(false);
                    throw;
                }

                return new JsonObject();
            }, lease);
            await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            shutdown = mainViewModel.ShutdownAsync();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(mainViewModel.ProjectService.CurrentProject.Value, Is.SameAs(project));
                Assert.That(closingCalls, Is.Zero);
                Assert.That(lease.DisposeCount, Is.Zero);
                Assert.That(manager.Get(jobId)!.State, Is.EqualTo("running"));
                Assert.That(
                    mainViewModel.EditorService.ProjectVersionControlCoordinator,
                    Is.SameAs(mainViewModel.VersionControlCoordinator));
            });

            releaseCancellation.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(mainViewModel.ProjectService.CurrentProject.Value, Is.Null);
                Assert.That(closingCalls, Is.EqualTo(1));
                Assert.That(manager.Get(jobId)!.State, Is.EqualTo("cancelled"));
                Assert.That(lease.DisposeCount, Is.EqualTo(1));
                Assert.That(mainViewModel.EditorService.ProjectVersionControlCoordinator, Is.Null);
            });
        }
        finally
        {
            releaseCancellation.TrySetResult();
            if (shutdown is not null && !shutdown.IsCompleted)
            {
                try
                {
                    await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    _ = ex;
                }
            }

            mainViewModel.ProjectService.Closing -= closing;
            mainViewModel.ProjectService.CloseProjectImmediately();
            await mainViewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Shutdown_reports_agent_host_failure_after_project_and_editor_cleanup()
    {
        await TestReset.ResetShellAsync();
        var mainViewModel = new MainViewModel((projectService, editorService) =>
            new AgentHostEndpoint(
                projectService,
                editorService,
                GetAvailableLoopbackPort(),
                "test-token",
                static _ => Task.FromException(
                    new InvalidOperationException("Expected agent-host drain failure."))));
        string projectFile = CreateProjectFilesOnDisk(
            "gateway-agent-drain-failure",
            TimeSpan.FromSeconds(4));

        try
        {
            await mainViewModel.ProjectService.OpenProject(projectFile);
            await mainViewModel.AgentHostEndpoint.StartAsync();

            InvalidOperationException? failure = null;
            try
            {
                await mainViewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (InvalidOperationException ex)
            {
                failure = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    failure?.Message,
                    Is.EqualTo("Expected agent-host drain failure."));
                Assert.That(mainViewModel.ProjectService.CurrentProject.Value, Is.Null);
                Assert.That(mainViewModel.EditorService.ProjectVersionControlCoordinator, Is.Null);
                Assert.That(mainViewModel.AgentHostEndpoint.IsRunning, Is.False);
                Assert.That(BeutlApplication.Current.Items, Is.Empty);
            });
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await mainViewModel.VersionControlCoordinator.GetAvailabilityAsync());
        }
        finally
        {
            mainViewModel.ProjectService.CloseProjectImmediately();
            try
            {
                await mainViewModel.DisposeAsync();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    [AvaloniaTest]
    public async Task Throwing_project_observer_does_not_block_editor_lifecycle_or_later_observers()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-observer-isolation", TimeSpan.FromSeconds(4));
        var snapshots = new List<(Project? New, Project? Old, Project? Current, int TabCount)>();
        using IDisposable throwing = TestShell.Project.ProjectObservable.Subscribe(
            static _ => throw new InvalidOperationException("Expected observer failure."));
        using IDisposable recording = TestShell.Project.ProjectObservable.Subscribe(change =>
            snapshots.Add((
                change.New,
                change.Old,
                TestShell.Project.CurrentProject.Value,
                TestShell.Editor.TabItems.Count)));

        await TestShell.Project.OpenProject(projectFile);
        await TestShell.Project.CloseProject();

        Assert.Multiple(() =>
        {
            Assert.That(snapshots, Has.Count.EqualTo(2));
            Assert.That(snapshots[0].New, Is.SameAs(snapshots[0].Current));
            Assert.That(snapshots[0].Old, Is.Null);
            Assert.That(snapshots[0].TabCount, Is.EqualTo(1));
            Assert.That(snapshots[1].New, Is.Null);
            Assert.That(snapshots[1].Old, Is.Not.Null);
            Assert.That(snapshots[1].Current, Is.Null);
            Assert.That(snapshots[1].TabCount, Is.Zero);
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task Failed_required_open_lifecycle_rolls_back_editor_activation()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-open-rollback", TimeSpan.FromSeconds(4));
        int publications = 0;
        Func<Project, Task> failingHandler = static _ =>
            Task.FromException(new InvalidOperationException("Expected lifecycle failure."));
        using IDisposable recording = TestShell.Project.ProjectObservable.Subscribe(
            _ => publications++);
        TestShell.Project.Opened += failingHandler;

        try
        {
            await TestShell.Project.OpenProject(projectFile);
        }
        finally
        {
            TestShell.Project.Opened -= failingHandler;
        }

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(TestShell.Editor.TabItems, Is.Empty);
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.Null);
            Assert.That(publications, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task OpenProject_rejects_a_second_different_project()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string first = CreateProjectFilesOnDisk("gateway-first", TimeSpan.FromSeconds(4));
        string second = CreateProjectFilesOnDisk("gateway-second", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(first);
        HeadlessTestHelpers.Settle();

        // Assert.ThrowsAsync blocks the UI thread and deadlocks the headless dispatcher,
        // so the rejection is awaited inline instead.
        ReconcileException? rejection = await ExpectRejectionAsync(() => gateway.OpenProjectAsync(second));

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejection.Error.Message, Does.Contain("single open project"));
            Assert.That(BeutlApplication.Current.Project!.Uri!.LocalPath, Is.EqualTo(first));
        });
    }

    [AvaloniaTest]
    public async Task OpenProject_with_the_open_projects_path_attaches_without_reopening()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-reattach", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();
        Project opened = BeutlApplication.Current.Project!;

        ProjectSessionResult reattached = await gateway.OpenProjectAsync(projectFile);

        Assert.Multiple(() =>
        {
            Assert.That(BeutlApplication.Current.Project, Is.SameAs(opened));
            Assert.That(reattached.Session.Source, Is.EqualTo(EditingSessionSource.LiveEditor));
            Assert.That(reattached.Project, Is.SameAs(opened));
        });
    }

    [AvaloniaTest]
    public async Task CreateProject_creates_at_the_requested_path_and_opens_in_editor()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string path = Path.Combine(NewWorkspace("gateway-create"), "fresh.bep");
        (EditorProjectSessionGateway gateway, _) = CreateGateway();

        ProjectSessionResult result = await gateway.CreateProjectAsync(new ProjectCreateOptions(
            path, 800, 450, 24, TimeSpan.FromSeconds(6)));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(BeutlApplication.Current.Project!.Uri!.LocalPath, Is.EqualTo(path));
            Assert.That(result.Session.Source, Is.EqualTo(EditingSessionSource.LiveEditor));
            Scene scene = result.Project.Items.OfType<Scene>().Single();
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(scene.FrameSize.Width, Is.EqualTo(800));
        });
    }

    [AvaloniaTest]
    public async Task CreateProject_rejects_when_a_different_project_is_open()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string first = CreateProjectFilesOnDisk("gateway-create-guard", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(first);
        HeadlessTestHelpers.Settle();
        string second = Path.Combine(NewWorkspace("gateway-create-second"), "other.bep");

        ReconcileException? rejection = await ExpectRejectionAsync(async () =>
            await gateway.CreateProjectAsync(new ProjectCreateOptions(
                second, 640, 360, 30, TimeSpan.FromSeconds(4))));

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(File.Exists(second), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task CreateProject_rejects_recreating_the_already_open_project()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string openPath = CreateProjectFilesOnDisk("gateway-create-samepath", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(openPath);
        HeadlessTestHelpers.Settle();
        Project openedProject = BeutlApplication.Current.Project!;

        ReconcileException? rejection = await ExpectRejectionAsync(async () =>
            await gateway.CreateProjectAsync(new ProjectCreateOptions(
                openPath, 640, 360, 30, TimeSpan.FromSeconds(4))));

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            // The open project must not have been replaced on disk or in memory by the create.
            Assert.That(BeutlApplication.Current.Project, Is.SameAs(openedProject));
        });
    }

    [AvaloniaTest]
    public async Task AddScene_adds_saves_and_shows_the_scene()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-addscene", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        ProjectSessionResult opened = await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();

        ProjectSceneResult added = await gateway.AddSceneAsync(opened.Session, new SceneCreateOptions(
            320, 180, TimeSpan.Zero, TimeSpan.FromSeconds(2), "second-scene"));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(added.Project.Items.OfType<Scene>().Count(), Is.EqualTo(2));
            Assert.That(File.Exists(added.Scene.Uri!.LocalPath), Is.True);
            var editViewModel = TestShell.Editor.SelectedTabItem.Value?.Context.Value as EditViewModel;
            Assert.That(editViewModel?.Scene, Is.SameAs(added.Scene));
            // The live session must be rebound to the newly activated scene, not left on the first.
            Assert.That(added.Session.Root, Is.SameAs(added.Scene));
        });
    }

    [AvaloniaTest]
    public async Task AddScene_rejects_a_session_whose_scene_left_the_open_project()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string firstProject = CreateProjectFilesOnDisk("gateway-stale-first", TimeSpan.FromSeconds(4));
        string secondProject = CreateProjectFilesOnDisk("gateway-stale-second", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        ProjectSessionResult opened = await gateway.OpenProjectAsync(firstProject);
        HeadlessTestHelpers.Settle();

        // Swap the open project out from under the captured session: its Root scene is no longer in
        // the live project, so add_scene must refuse rather than mutate a document the client is not
        // editing.
        await TestShell.Project.CloseProject();
        await TestShell.Project.OpenProject(secondProject);
        HeadlessTestHelpers.Settle();

        SessionUnavailableException? rejection = null;
        try
        {
            await gateway.AddSceneAsync(opened.Session, new SceneCreateOptions(
                320, 180, TimeSpan.Zero, TimeSpan.FromSeconds(2), "orphan-scene"));
            Assert.Fail("Expected a SessionUnavailableException.");
        }
        catch (SessionUnavailableException ex)
        {
            rejection = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(rejection, Is.Not.Null);
            Assert.That(BeutlApplication.Current.Project!.Uri!.LocalPath, Is.EqualTo(secondProject));
            // The swapped-in project keeps its single original scene; the rejected add left nothing behind.
            Assert.That(BeutlApplication.Current.Project!.Items.OfType<Scene>().Count(), Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task Close_then_open_waits_for_editor_teardown_before_returning()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectFile = CreateProjectFilesOnDisk("gateway-close-teardown", TimeSpan.FromSeconds(4));
        string nextProjectFile = CreateProjectFilesOnDisk("gateway-open-after-teardown", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();

        var editViewModel = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        Project firstProject = TestShell.Project.CurrentProject.Value!;
        ProjectService projectService = TestShell.Project;
        using var renderEntered = new ManualResetEventSlim();
        using var releaseRender = new ManualResetEventSlim();
        Task renderBlocker = RenderThread.Dispatcher.InvokeAsync(() =>
        {
            renderEntered.Set();
            releaseRender.Wait(TimeSpan.FromSeconds(10));
        });
        Task? closeProject = null;
        Task? openProject = null;

        try
        {
            Assert.That(renderEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            closeProject = Task.Run(() => projectService.CloseProject());
            await WaitUntilAsync(() => TestShell.Editor.TabItems.Count == 0);
            openProject = Task.Run(() => projectService.OpenProject(nextProjectFile));
            await Task.Yield();

            Assert.Multiple(() =>
            {
                Assert.That(closeProject.IsCompleted, Is.False);
                Assert.That(openProject.IsCompleted, Is.False);
                Assert.That(projectService.CurrentProject.Value, Is.SameAs(firstProject));
                Assert.That(TestShell.Editor.TabItems, Is.Empty);
                Assert.That(editViewModel.Player, Is.Not.Null);
            });
        }
        finally
        {
            releaseRender.Set();
            await renderBlocker.WaitAsync(TimeSpan.FromSeconds(5));
            await RenderThread.Dispatcher.InvokeAsync(static () => { })
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (closeProject is not null)
            {
                await closeProject.WaitAsync(TimeSpan.FromSeconds(5));
            }
            if (openProject is not null)
            {
                await openProject.WaitAsync(TimeSpan.FromSeconds(5));
            }

            HeadlessTestHelpers.Settle();
        }

        Assert.Multiple(() =>
        {
            Assert.That(projectService.CurrentProject.Value!.Uri!.LocalPath, Is.EqualTo(nextProjectFile));
            Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(1));
            Assert.That(editViewModel.Player, Is.Null);
            Assert.That(
                TestShell.Editor.SelectedTabItem.Value!.Context.Value.Object,
                Is.SameAs(projectService.CurrentProject.Value.Items.Single()));
        });
    }

    [AvaloniaTest]
    public async Task Failed_close_prepare_restores_editor_tabs_selection_and_project_subscription()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectFile = CreateProjectFilesOnDisk(
            "gateway-close-finalizer-failure",
            TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        await gateway.OpenProjectAsync(projectFile);
        HeadlessTestHelpers.Settle();

        Project project = TestShell.Project.CurrentProject.Value!;
        CoreObject selectedObject = TestShell.Editor.SelectedTabItem.Value!.Context.Value.Object;
        int originalTabCount = TestShell.Editor.TabItems.Count;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> failingPrepare =
            static (_, _) => Task.FromException(
                new InvalidOperationException("Expected close-prepare failure."));
        TestShell.Project.Closing += failingPrepare;

        Exception? failure = null;
        try
        {
            await TestShell.Project.CloseProject();
            Assert.Fail("Expected the close transition to fail.");
        }
        catch (Exception ex) when (ex is not AssertionException)
        {
            failure = ex;
        }
        finally
        {
            TestShell.Project.Closing -= failingPrepare;
        }

        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(originalTabCount));
            Assert.That(
                TestShell.Editor.SelectedTabItem.Value!.Context.Value.Object,
                Is.SameAs(selectedObject));
        });

        var addedScene = new Scene(320, 180, "after-aborted-close")
        {
            Uri = new Uri(Path.Combine(NewWorkspace("after-aborted-close"), "added.scene"))
        };
        project.Items.Add(addedScene);
        await WaitUntilAsync(() => TestShell.Editor.TryGetTabItem(addedScene, out _));

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(originalTabCount + 1));
            Assert.That(TestShell.Editor.SelectedTabItem.Value!.Context.Value.Object, Is.SameAs(addedScene));
        });
    }

    [AvaloniaTest]
    public async Task AddScene_rolls_back_the_live_scene_when_the_save_fails()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        string projectPath = CreateProjectFilesOnDisk("addscene-rollback", TimeSpan.FromSeconds(4));
        (EditorProjectSessionGateway gateway, _) = CreateGateway();
        ProjectSessionResult opened = await gateway.OpenProjectAsync(projectPath);
        HeadlessTestHelpers.Settle();
        string[] originalSceneUris = BeutlApplication.Current.Project!.Items.OfType<Scene>()
            .Select(s => s.Uri!.LocalPath)
            .ToArray();

        // A directory occupying the project file path makes ProjectOperations.Save throw after the
        // scene was added to the live project.
        File.Delete(projectPath);
        Directory.CreateDirectory(projectPath);

        Exception? failure = null;
        try
        {
            await gateway.AddSceneAsync(opened.Session, new SceneCreateOptions(
                320, 180, TimeSpan.Zero, TimeSpan.FromSeconds(2), "second-scene"));
            Assert.Fail("Expected the save to fail.");
        }
        catch (Exception ex) when (ex is not AssertionException)
        {
            failure = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(BeutlApplication.Current.Project!.Items.OfType<Scene>().Count(), Is.EqualTo(1));
            Assert.That(
                BeutlApplication.Current.Project!.Items.OfType<Scene>().Select(s => s.Uri!.LocalPath),
                Is.EqualTo(originalSceneUris));
        });
    }

    [AvaloniaTest]
    public async Task AddScene_rejects_saving_a_project_opened_outside_the_workspace()
    {
        await using ShellTestScope cleanup = await ShellTestScope.CreateAsync();
        // open_project reads anywhere, so the editor can hold a project outside the workspace; add_scene
        // must not persist its sidecars there.
        string outsideDir = Path.Combine(Path.GetTempPath(), "beutl-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        try
        {
            string outsideProject = Path.Combine(outsideDir, "outside.bep");
            Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
                outsideProject, 640, 360, 30, TimeSpan.FromSeconds(4)));
            ProjectOperations.Save(project);
            (EditorProjectSessionGateway gateway, _) = CreateGateway();
            ProjectSessionResult opened = await gateway.OpenProjectAsync(outsideProject);
            HeadlessTestHelpers.Settle();

            WorkspaceBoundaryException? rejection = null;
            try
            {
                await gateway.AddSceneAsync(opened.Session, new SceneCreateOptions(
                    320, 180, TimeSpan.Zero, TimeSpan.FromSeconds(2), "second-scene"));
                Assert.Fail("Expected a workspace-boundary rejection.");
            }
            catch (WorkspaceBoundaryException ex)
            {
                rejection = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null);
                // The boundary guard runs before the live project is mutated, so a rejected add_scene
                // leaves no unsaved extra scene behind in the editor.
                Assert.That(BeutlApplication.Current.Project!.Items.OfType<Scene>().Count(), Is.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(outsideDir, true);
        }
    }
}
