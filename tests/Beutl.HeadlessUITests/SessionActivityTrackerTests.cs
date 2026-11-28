using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.Agents;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;
using Beutl.Editor.Components.OutputTab;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Every kind of work on a project holds a lease on its session's activity tracker while it runs, so "nothing runs for
// this session" is decided from the leases alone (#4794).
[TestFixture]
[NonParallelizable]
public sealed class SessionActivityTrackerTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
    private bool _oldAutoSaveSuppressed;
    private bool _oldAutoCommitOnClose;

    [SetUp]
    public void SuppressBackgroundSaves()
    {
        VersionControlConfig config = TestShell.Configuration.VersionControlConfig;
        _oldAutoCommitOnClose = config.AutoCommitOnClose.Value;
        _oldAutoSaveSuppressed = EditViewModel.IsAutoSaveSuppressedForTesting;
        config.AutoCommitOnClose.Value = false;
        EditViewModel.IsAutoSaveSuppressedForTesting = true;
    }

    [TearDown]
    public void RestoreBackgroundSaves()
    {
        TestShell.Configuration.VersionControlConfig.AutoCommitOnClose.Value = _oldAutoCommitOnClose;
        EditViewModel.IsAutoSaveSuppressedForTesting = _oldAutoSaveSuppressed;
    }

    [AvaloniaTest]
    public void A_lease_keeps_the_session_busy_until_the_last_one_ends_which_rearms_the_idle_time()
    {
        var time = new FakeTimeProvider();
        int idled = 0;
        var tracker = new SessionActivityTracker(time, () => idled++);
        long created = time.GetTimestamp();
        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsBusy.Value, Is.False);
            Assert.That(tracker.HasActiveLeases, Is.False);
            Assert.That(tracker.LastIdleSince, Is.EqualTo(created));
            Assert.That(tracker.TimeProvider, Is.SameAs(time));
        });

        IDisposable output = tracker.Begin(SessionActivityKind.Output);
        time.Advance(TimeSpan.FromSeconds(5));
        IDisposable autoSave = tracker.Begin(SessionActivityKind.AutoSave);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsBusy.Value, Is.True);
            Assert.That(tracker.CountOf(SessionActivityKind.Output), Is.EqualTo(1));
            Assert.That(tracker.CountOf(SessionActivityKind.AutoSave), Is.EqualTo(1));
        });

        output.Dispose();
        output.Dispose();
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsBusy.Value, Is.True, "Another lease is still held.");
            Assert.That(tracker.CountOf(SessionActivityKind.Output), Is.Zero, "A lease disposed twice ends once.");
            Assert.That(tracker.LastIdleSince, Is.EqualTo(created));
            Assert.That(idled, Is.Zero);
        });

        autoSave.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsBusy.Value, Is.False);
            Assert.That(tracker.LastIdleSince, Is.EqualTo(time.GetTimestamp()), "The idle time starts when the last lease ends.");
            Assert.That(idled, Is.EqualTo(1));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Begin((SessionActivityKind)int.MaxValue));
    }

    [AvaloniaTest]
    public async Task Waiting_for_idle_drains_every_lease_and_can_be_canceled()
    {
        var tracker = new SessionActivityTracker();
        Assert.That(tracker.WaitForIdleAsync().IsCompleted, Is.True, "An idle session needs no wait.");

        IDisposable first = tracker.Begin(SessionActivityKind.Output);
        IDisposable second = tracker.Begin(SessionActivityKind.VersionControl);
        Task idle = tracker.WaitForIdleAsync();
        Task othersEnded = tracker.WaitForIdleAsync(ignoredLeases: 1);
        using var cancellation = new CancellationTokenSource();
        Task canceled = tracker.WaitForIdleAsync(cancellation.Token);

        first.Dispose();
        await othersEnded.WaitAsync(s_timeout);
        Assert.That(idle.IsCompleted, Is.False, "One lease is still held.");

        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => canceled.WaitAsync(s_timeout));
        Assert.That(tracker.HasActiveLeases, Is.True, "Canceling a wait leaves the leases alone.");

        second.Dispose();
        await idle.WaitAsync(s_timeout);
        Assert.That(tracker.IsBusy.Value, Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.WaitForIdleAsync(ignoredLeases: -1));
    }

    [AvaloniaTest]
    public void A_hold_follows_its_flag_and_ends_when_the_flag_completes_fails_or_is_dropped()
    {
        var tracker = new SessionActivityTracker();

        var completing = new Subject<bool>();
        tracker.HoldWhile(completing, SessionActivityKind.AiGeneration);
        completing.OnNext(true);
        completing.OnNext(true);
        Assert.That(tracker.CountOf(SessionActivityKind.AiGeneration), Is.EqualTo(1), "A flag set twice holds one lease.");
        completing.OnNext(false);
        Assert.That(tracker.IsBusy.Value, Is.False);
        completing.OnNext(true);
        Assert.That(tracker.IsBusy.Value, Is.True);
        completing.OnCompleted();
        Assert.That(tracker.IsBusy.Value, Is.False, "A flag that completes while set ends its hold.");

        var failing = new Subject<bool>();
        tracker.HoldWhile(failing, SessionActivityKind.AiGeneration);
        failing.OnNext(true);
        failing.OnError(new InvalidOperationException());
        Assert.That(tracker.IsBusy.Value, Is.False, "A flag that fails ends its hold.");

        var dropped = new BehaviorSubject<bool>(true);
        IDisposable hold = tracker.HoldWhile(dropped, SessionActivityKind.AiGeneration);
        Assert.That(tracker.IsBusy.Value, Is.True, "A flag already set holds at once.");
        hold.Dispose();
        dropped.OnNext(false);
        dropped.OnNext(true);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsBusy.Value, Is.False, "A dropped hold no longer follows its flag.");
            Assert.That(dropped.HasObservers, Is.False);
        });

        // An observable that completes while it is subscribed leaves nothing behind.
        tracker.HoldWhile(System.Reactive.Linq.Observable.Return(true), SessionActivityKind.AiGeneration);
        Assert.That(tracker.HasActiveLeases, Is.False);
    }

    [AvaloniaTest]
    public async Task A_lease_ended_on_another_thread_is_published_on_the_ui_thread()
    {
        var tracker = new SessionActivityTracker();
        var seen = new List<bool>();
        using IDisposable subscription = tracker.IsBusy.Subscribe(seen.Add);
        IDisposable lease = await Task.Run(() => tracker.Begin(SessionActivityKind.AgentAiJob));
        Assert.That(tracker.HasActiveLeases, Is.True);
        HeadlessTestHelpers.Settle();
        Assert.That(tracker.IsBusy.Value, Is.True);

        await Task.Run(lease.Dispose);
        Assert.That(tracker.HasActiveLeases, Is.False, "The count changes at once on any thread.");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsBusy.Value, Is.False);
            Assert.That(seen, Is.EqualTo(new[] { false, true, false }));
        });
    }

    [AvaloniaTest]
    public async Task Reservations_and_lifecycle_work_on_a_project_keep_its_session_busy()
    {
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-reservations");
            EditorWorkspace workspace = editor.Workspace;
            SessionActivityTracker activity = workspace.Activity;
            await activity.WaitForIdleAsync().WaitAsync(s_timeout);

            using (workspace.TryBeginOutputOperation())
                AssertBusyWith(activity, SessionActivityKind.Output);
            using (workspace.TryBeginEditorFileOpen())
                AssertBusyWith(activity, SessionActivityKind.EditorFileOpen);
            using (workspace.TryBeginProjectFileWrite())
                AssertBusyWith(activity, SessionActivityKind.ProjectFileWrite);
            using (await workspace.BeginProjectFileWriteAsync(CancellationToken.None))
                AssertBusyWith(activity, SessionActivityKind.ProjectFileWrite);
            using (workspace.BeginLifecycleActivity(ProjectLifecycleActivity.ClosingProject))
                AssertBusyWith(activity, SessionActivityKind.Lifecycle);
            Assert.That(activity.IsBusy.Value, Is.False);

            // A write folded into a worktree mutation hands its lease over without a moment of idleness.
            var seen = new List<bool>();
            using (activity.IsBusy.Subscribe(seen.Add))
            {
                IProjectFileWriteLease write = workspace.TryBeginProjectFileWrite()!;
                using (IDisposable? mutation = workspace.TryBeginWorktreeMutation(write))
                {
                    Assert.That(mutation, Is.Not.Null);
                    write.Dispose();
                    AssertBusyWith(activity, SessionActivityKind.WorktreeMutation);
                    Assert.That(activity.CountOf(SessionActivityKind.ProjectFileWrite), Is.Zero);

                    // Work refused while the mutation runs leaves no lease behind.
                    Assert.That(workspace.TryBeginOutputOperation(), Is.Null);
                    Assert.That(workspace.TryBeginEditorFileOpen(), Is.Null);
                    Assert.That(workspace.TryBeginProjectFileWrite(), Is.Null);
                    Assert.That(activity.CountOf(SessionActivityKind.Output), Is.Zero);
                    Assert.That(activity.CountOf(SessionActivityKind.EditorFileOpen), Is.Zero);
                    Assert.That(activity.CountOf(SessionActivityKind.ProjectFileWrite), Is.Zero);
                }
            }

            Assert.That(seen, Is.EqualTo(new[] { false, true, false }));

            // A mutation refused while an output runs leaves no lease behind either.
            using (workspace.TryBeginOutputOperation())
            {
                Assert.That(workspace.TryBeginWorktreeMutation(), Is.Null);
                Assert.That(activity.CountOf(SessionActivityKind.WorktreeMutation), Is.Zero);
            }

            Assert.That(activity.IsBusy.Value, Is.False);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_running_output_keeps_its_project_busy_until_it_ends(bool fails)
    {
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-output");
            EditorWorkspace workspace = editor.Workspace;
            var context = new GatedOutputContext(editor.Scene);
            using var output = new OutputProfileItem(context, editor, workspace);
            Assert.That(output.TryStart(out Task? execution), Is.True);
            await context.Started.Task.WaitAsync(s_timeout);
            Assert.That(workspace.Activity.CountOf(SessionActivityKind.Output), Is.EqualTo(2),
                "The output reserves the project's files and counts among its running outputs.");

            if (fails)
                context.Fail();
            else
                context.Finish();
            try
            {
                await execution!.WaitAsync(s_timeout);
            }
            catch (InvalidOperationException) when (fails)
            {
            }

            await HeadlessTestHelpers.WaitUntilAsync(() => !workspace.Activity.HasActiveLeases, s_timeout);
            Assert.That(workspace.Activity.IsBusy.Value, Is.False);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task An_auto_save_keeps_the_session_busy_from_the_edit_until_its_files_are_written()
    {
        try
        {
            (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-auto-save");
            SessionActivityTracker activity = editor.Workspace.Activity;
            EditViewModel.IsAutoSaveSuppressedForTesting = false;

            // The editor's files are held, so the save queued by the edit waits for them.
            IProjectFileWriteLease held = editor.Workspace.TryBeginProjectFileWrite()!;
            scene.Duration.Value += MediaTime.FromSeconds(1);
            editor.HistoryManager.Commit();
            HeadlessTestHelpers.Settle();
            Assert.That(activity.CountOf(SessionActivityKind.AutoSave), Is.EqualTo(1),
                "A save waiting for the project's files keeps the session busy.");
            held.Dispose();

            await HeadlessTestHelpers.WaitUntilAsync(() => !activity.HasActiveLeases, s_timeout);
            Assert.That(editor.Workspace.HasUnsavedChanges, Is.False);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task An_unsaved_edit_in_any_editor_of_the_project_marks_the_session_unsaved()
    {
        try
        {
            (Project project, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-unsaved");
            EditorWorkspace workspace = editor.Workspace;
            Assert.That(workspace.HasUnsavedChanges, Is.False);

            scene.Duration.Value += MediaTime.FromSeconds(1);
            editor.HistoryManager.Commit();
            Assert.That(workspace.HasUnsavedChanges, Is.True);

            // Put aside while another project is active, the editor still counts.
            await TestShell.Project.CreateProject(
                320, 180, new Rational(30), 44100, "activity-unsaved-other", TestShell.NewWorkspace("activity-unsaved-other"));
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Editor.HasInactiveTabs(project), Is.True);
                Assert.That(workspace.HasUnsavedChanges, Is.True);
                Assert.That(TestShell.Editor.ActiveWorkspace.Value.HasUnsavedChanges, Is.False);
            });

            Assert.That(await workspace.SaveSceneEditorsBeforeCloseAsync(workspace.TabItems), Is.True);
            Assert.That(workspace.HasUnsavedChanges, Is.False);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_lease_keeps_the_workspace_of_an_unloaded_project_until_it_ends()
    {
        try
        {
            (Project project, _, _) = await TestShell.OpenNewSceneAsync("activity-workspace-release");
            string file = project.Uri!.LocalPath;
            EditorWorkspace workspace = TestShell.Editor.ActiveWorkspace.Value;
            IDisposable lease = TestShell.Editor.ActivityOfFile(file).Begin(SessionActivityKind.AgentThread);
            Assert.That(workspace.Activity.CountOf(SessionActivityKind.AgentThread), Is.EqualTo(1),
                "Work keyed by the project file leases the workspace of the loaded project.");

            Assert.That(await TestShell.Project.TryCloseProjectAsync(project, ProjectService.ProjectCloseIntent.SaveChanges), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(workspace.Project.Value, Is.Null);
                Assert.That(workspace.IsIdle, Is.False);
                Assert.That(TestShell.Editor.Workspaces, Does.Contain(workspace), "A project reloaded meanwhile finds its lease.");
            });

            lease.Dispose();
            HeadlessTestHelpers.Settle();
            Assert.That(TestShell.Editor.Workspaces, Does.Not.Contain(workspace));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Version_control_work_keeps_the_session_busy_until_it_ends(bool disposeFirst)
    {
        await TestReset.ResetShellAsync();
        var probe = new GatedGitInstallationProbe();
        var config = new VersionControlConfig();
        var locator = new GitInstallationLocator(config, probe, GitHostPlatform.Linux);
        var editorService = new EditorService(new Beutl.Api.Services.ExtensionProvider(TestShell.Configuration.ExtensionConfig), TestShell.MainViewModel.AppServices);
        SessionActivityTracker activity = editorService.ShellWorkspace.Activity;
        var coordinator = new VersionControlCoordinator(TestShell.Project, editorService, config, locator);
        try
        {
            Task<GitAvailability> availability = coordinator.GetAvailabilityAsync();
            await probe.Started.Task.WaitAsync(s_timeout);
            Assert.That(activity.CountOf(SessionActivityKind.VersionControl), Is.EqualTo(1),
                "The coordinator's running work holds one lease, on the workspace it works for.");

            if (disposeFirst)
            {
                await coordinator.DisposeAsync().AsTask().WaitAsync(s_timeout);
                await Assert.CatchAsync<OperationCanceledException>(() => availability.WaitAsync(s_timeout));
            }
            else
            {
                probe.Release();
                await availability.WaitAsync(s_timeout);
            }

            await HeadlessTestHelpers.WaitUntilAsync(() => activity.CountOf(SessionActivityKind.VersionControl) == 0, s_timeout);
        }
        finally
        {
            probe.Release();
            await coordinator.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task An_ai_page_keeps_its_editors_session_busy_while_it_runs()
    {
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-ai");
            SessionActivityTracker activity = editor.Workspace.Activity;
            AiImageGenerationDialogViewModel page = TestShell.MainViewModel.CreateAiImageGenerationToolViewModel(editor);

            page.IsGenerating.Value = true;
            AssertBusyWith(activity, SessionActivityKind.AiGeneration);
            page.IsGenerating.Value = false;
            Assert.That(activity.CountOf(SessionActivityKind.AiGeneration), Is.Zero);

            page.IsGenerating.Value = true;
            await page.DisposeAsync();
            Assert.That(activity.CountOf(SessionActivityKind.AiGeneration), Is.Zero, "A page disposed mid-run ends its lease.");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task An_agent_ai_job_keeps_the_session_busy_until_it_finishes_fails_or_is_canceled()
    {
        var tracker = new SessionActivityTracker();
        using var jobs = new AgentAiJobManager();
        Func<IDisposable?> lease = () => tracker.Begin(SessionActivityKind.AgentAiJob);

        var finish = new TaskCompletionSource<AgentAiJobOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = new AgentAiRequestKey("account", "generate_image", "prompt");
        (AgentAiJobManager.Job job, bool started) = jobs.GetOrStart("generate_image", (_, _) => finish.Task, null, key, lease);
        Assert.That(started, Is.True);
        (_, bool replayed) = jobs.GetOrStart("generate_image", (_, _) => finish.Task, null, key, lease);
        Assert.Multiple(() =>
        {
            Assert.That(replayed, Is.False);
            Assert.That(tracker.CountOf(SessionActivityKind.AgentAiJob), Is.EqualTo(1), "A replayed request takes no lease.");
        });
        finish.SetResult(new AgentAiJobOutput(null, null, null, null));
        await job.Task!.WaitAsync(s_timeout);
        Assert.That(tracker.HasActiveLeases, Is.False);

        string failed = jobs.Start("generate_image", (_, _) => Task.FromException<AgentAiJobOutput>(new InvalidOperationException()),
            beginActivity: lease);
        await jobs.WaitAsync(failed, s_timeout, CancellationToken.None);
        await tracker.WaitForIdleAsync().WaitAsync(s_timeout);

        string canceled = jobs.Start("generate_image", async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new AgentAiJobOutput(null, null, null, null);
        }, beginActivity: lease);
        Assert.That(tracker.HasActiveLeases, Is.True);
        Assert.That(jobs.Cancel(canceled), Is.True);
        await tracker.WaitForIdleAsync().WaitAsync(s_timeout);

        jobs.Dispose();
        Assert.Throws<ObjectDisposedException>(() => jobs.Start("generate_image", (_, _) => finish.Task, beginActivity: lease));
        Assert.That(tracker.HasActiveLeases, Is.False, "A job refused after disposal ends its lease.");
    }

    [AvaloniaTest]
    public async Task A_running_agent_thread_keeps_its_projects_session_busy()
    {
        await using var workspace = new AgentTabTestWorkspace();
        var tracker = new SessionActivityTracker();
        var leasedPaths = new List<string>();
        workspace.ThreadSessions.BeginProjectActivity = path =>
        {
            leasedPaths.Add(path);
            return tracker.Begin(SessionActivityKind.AgentThread);
        };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = async (_, token) => await release.Task.WaitAsync(token);

        model.Prompt = "Trim the opening";
        Task run = model.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning, s_timeout);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.CountOf(SessionActivityKind.AgentThread), Is.EqualTo(1));
            Assert.That(leasedPaths, Is.EqualTo(new[] { workspace.ProjectPath }));
        });

        release.SetResult();
        await run.WaitAsync(s_timeout);
        await HeadlessTestHelpers.WaitUntilAsync(() => !tracker.HasActiveLeases, s_timeout);

        // Shutting the threads down ends the lease of one still running.
        var hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = async (_, token) => await hang.Task.WaitAsync(token);
        model.Prompt = "Check the credits";
        _ = model.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning, s_timeout);
        Assert.That(tracker.HasActiveLeases, Is.True);
        await workspace.ThreadSessions.DisposeAsync();
        hang.TrySetResult();
        Assert.That(tracker.HasActiveLeases, Is.False);
    }

    [AvaloniaTest]
    public async Task The_app_keys_agent_threads_to_the_session_of_their_project_file()
    {
        try
        {
            (Project project, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-agent-wiring");
            var host = (BeutlAgentWorkspaceHost)TestShell.Editor.AgentWorkspaceHost!;
            Func<string, IDisposable?> begin = host.ThreadSessions.BeginProjectActivity!;
            using (begin(Path.GetFullPath(project.Uri!.LocalPath)))
                AssertBusyWith(editor.Workspace.Activity, SessionActivityKind.AgentThread);
            Assert.That(editor.Workspace.Activity.CountOf(SessionActivityKind.AgentThread), Is.Zero);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Playback_keeps_the_session_busy_until_it_has_stopped()
    {
        TestGpu.RequireAnyDevice();
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
                "activity-playback", 160, 90, duration: MediaTime.FromSeconds(1));
            SessionActivityTracker activity = editor.Workspace.Activity;
            editor.Player.Play();
            Assert.That(editor.Player.IsPlaying.Value, Is.True);
            AssertBusyWith(activity, SessionActivityKind.Playback);

            await editor.Player.Pause();
            Assert.That(activity.CountOf(SessionActivityKind.Playback), Is.Zero);

            // Playback that reaches the end of the scene ends its lease once its task has finished.
            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.Zero);
            editor.Player.Play();
            AssertBusyWith(activity, SessionActivityKind.Playback);
            await HeadlessTestHelpers.WaitUntilAsync(
                () => !editor.Player.IsPlaying.Value && activity.CountOf(SessionActivityKind.Playback) == 0, s_timeout);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_media_fingerprint_scan_keeps_the_session_busy_from_its_queueing_until_its_file_is_written()
    {
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-fingerprints");
            SessionActivityTracker activity = editor.Workspace.Activity;
            await editor.WaitForMediaFingerprintsAsync().WaitAsync(s_timeout);
            await HeadlessTestHelpers.WaitUntilAsync(() => !activity.HasActiveLeases, s_timeout);
            var hashing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.CaptureMediaFingerprints = (_, token) => hashing.Task.WaitAsync(token);

            editor.ScheduleMediaFingerprints();
            Assert.That(activity.CountOf(SessionActivityKind.MediaFingerprints), Is.EqualTo(1),
                "The scan keeps the session busy from the moment it is queued.");

            // Once hashed, the scan waits for the project's files and stays busy meanwhile.
            IProjectFileWriteLease held = editor.Workspace.TryBeginProjectFileWrite()!;
            hashing.SetResult();
            HeadlessTestHelpers.Settle();
            await Task.Delay(50);
            HeadlessTestHelpers.Settle();
            Assert.That(activity.CountOf(SessionActivityKind.MediaFingerprints), Is.EqualTo(1),
                "A scan waiting for the project's files keeps the session busy.");
            held.Dispose();

            await editor.WaitForMediaFingerprintsAsync().WaitAsync(s_timeout);
            Assert.That(activity.CountOf(SessionActivityKind.MediaFingerprints), Is.Zero);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_media_fingerprint_scan_abandoned_by_closing_its_editor_ends_its_lease()
    {
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-fingerprints-closed");
            SessionActivityTracker activity = editor.Workspace.Activity;
            await editor.WaitForMediaFingerprintsAsync().WaitAsync(s_timeout);
            editor.CaptureMediaFingerprints = (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token);

            editor.ScheduleMediaFingerprints();
            Task scan = editor.WaitForMediaFingerprintsAsync();
            HeadlessTestHelpers.Settle();
            Assert.That(activity.CountOf(SessionActivityKind.MediaFingerprints), Is.EqualTo(1));

            await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!, saveChanges: false);
            await scan.WaitAsync(s_timeout);
            Assert.That(activity.CountOf(SessionActivityKind.MediaFingerprints), Is.Zero, "A canceled scan ends its lease.");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_node_graph_generation_keeps_the_session_busy_until_it_ends()
    {
        var tracker = new SessionActivityTracker();
        var catalog = new Moq.Mock<IAiModelCatalogService>();
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.Setup(service => service.GetAsync(Moq.It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                loading.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return AiModelCatalog.Empty;
            });
        var executor = new AiGenerativeNodeExecutor(
            new Scene(64, 64, "generative"),
            new AiGenerativeModelCatalog(catalog.Object, Moq.Mock.Of<IAiEntitlementService>()),
            Moq.Mock.Of<IAiImageGenerationService>(), Moq.Mock.Of<IAiOperationAvailabilityService>(),
            Moq.Mock.Of<IAuthenticatedContentService>(), Moq.Mock.Of<IGenerativePromptLibrary>(),
            Moq.Mock.Of<IAiImageEditingService>(), Moq.Mock.Of<IAiVideoService>(), Moq.Mock.Of<IAiJobKindRegistry>(),
            TestShell.Decoders)
        {
            BeginActivity = () => tracker.Begin(SessionActivityKind.AiGeneration),
        };
        var request = new AiImageGenerationNodeRequest("image.generate")
        {
            Prompt = "a cat",
            AspectRatio = "1:1",
            RequestKeySeed = Guid.NewGuid().ToString("N"),
            ParameterFingerprint = "p",
        };

        using var cancellation = new CancellationTokenSource();
        Task<GenerativeExecutionResult> run = executor.ExecuteAsync(request, new Progress<GenerativeProgress>(), cancellation.Token);
        await loading.Task.WaitAsync(s_timeout);
        Assert.That(tracker.CountOf(SessionActivityKind.AiGeneration), Is.EqualTo(1));

        await cancellation.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => run.WaitAsync(s_timeout));
        Assert.That(tracker.HasActiveLeases, Is.False, "A canceled generation ends its lease.");
    }

    [AvaloniaTest]
    public async Task The_app_leases_a_node_graph_generation_on_its_scenes_project()
    {
        try
        {
            (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-node-wiring");
            var executor = (AiGenerativeNodeExecutor)TestShell.MainViewModel.CreateGenerativeNodeExecutor(scene);
            using (executor.BeginActivity!())
                AssertBusyWith(editor.Workspace.Activity, SessionActivityKind.AiGeneration);
            Assert.That(editor.Workspace.Activity.CountOf(SessionActivityKind.AiGeneration), Is.Zero);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task An_ai_page_disposed_mid_run_ends_its_hold_even_if_its_flag_stays_set()
    {
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("activity-ai-drained");
            SessionActivityTracker activity = editor.Workspace.Activity;
            AiImageEditDialogViewModel page = TestShell.MainViewModel.CreateAiImageEditToolViewModel(editor);
            // A flag the page's disposal never completes, as one cleared only by a publication it refuses.
            var running = new BehaviorSubject<bool>(false);
            MainViewModel.HoldSessionWhileRunning(editor, page, _ => running);
            running.OnNext(true);
            Assert.That(activity.CountOf(SessionActivityKind.AiGeneration), Is.EqualTo(1));

            await page.DisposeAsync();
            Assert.Multiple(() =>
            {
                Assert.That(activity.CountOf(SessionActivityKind.AiGeneration), Is.Zero,
                    "The page ends its hold once its operations have drained.");
                Assert.That(running.HasObservers, Is.False);
            });

            // A hold given to a page already disposed ends at once.
            MainViewModel.HoldSessionWhileRunning(editor, page, _ => new BehaviorSubject<bool>(true));
            Assert.That(activity.CountOf(SessionActivityKind.AiGeneration), Is.Zero);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static void AssertBusyWith(SessionActivityTracker activity, SessionActivityKind kind)
    {
        Assert.Multiple(() =>
        {
            Assert.That(activity.IsBusy.Value, Is.True, $"{kind} keeps the session busy.");
            Assert.That(activity.CountOf(kind), Is.EqualTo(1), $"{kind} holds one lease.");
        });
    }

    private sealed class GatedOutputContext(CoreObject obj) : IOutputContext
    {
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OutputExtension Extension => throw new NotSupportedException();

        public CoreObject Object { get; } = obj;

        public IReactiveProperty<string> Name { get; } = new ReactivePropertySlim<string>("Output");

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _finish.Task.WaitAsync(cancellationToken);
        }

        public void Finish() => _finish.TrySetResult();

        public void Fail() => _finish.TrySetException(new InvalidOperationException("The output failed."));

        public void Dispose()
        {
        }

        public void WriteToJson(System.Text.Json.Nodes.JsonObject json)
        {
        }

        public void ReadFromJson(System.Text.Json.Nodes.JsonObject json)
        {
        }
    }

    // Finds no Git until released, so the availability probe stays running until the test lets it end.
    private sealed class GatedGitInstallationProbe : IGitInstallationProbe
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async Task<IReadOnlyList<string>> FindOnPathAsync(string executableName, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return [];
        }

        public Task<bool> HasMacCommandLineToolsAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<GitProbeResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The gated probe must not execute Git.");

        public bool FileExists(string path) => false;

        public string? GetEnvironmentVariable(string name) => null;
    }
}
