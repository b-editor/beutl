using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// Opening a project activates its session and leaves the one that was active loaded with its edits.
[TestFixture]
public class ProjectSessionActivationTests
{
    private bool _oldAutoCommitOnClose;
    private bool _oldAutoSaveSuppressed;

    private static ProjectSessionRegistry Registry => TestShell.Project.Sessions;

    [SetUp]
    public void SuppressBackgroundSaves()
    {
        // A version-control snapshot or an autosave would write the edits these tests expect to stay in memory.
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
    public async Task Opening_a_second_project_keeps_the_first_loaded()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("first");
            string second = await CreateClosedProjectAsync("second");

            await TestShell.Project.OpenProject(first);
            Project firstProject = TestShell.Project.CurrentProject.Value!;
            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();

            Project secondProject = TestShell.Project.CurrentProject.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(secondProject.Uri!.LocalPath, Is.EqualTo(second));
                Assert.That(Registry.Sessions.Select(s => s.Project), Is.EqualTo(new[] { firstProject, secondProject }));
                Assert.That(Registry.ActiveSession.Value!.Project, Is.SameAs(secondProject));
                Assert.That(BeutlApplication.Current.Projects, Is.EqualTo(new[] { firstProject, secondProject }));
                Assert.That(firstProject.HierarchicalParent, Is.SameAs(BeutlApplication.Current));
                // The editor shows the active session only; the first one's tabs wait aside.
                Assert.That(TestShell.Editor.TabItems.Select(t => t.Context.Value.Object),
                    Is.EqualTo(secondProject.Items));
                Assert.That(TestShell.Editor.HasInactiveTabs(firstProject), Is.True);
                Assert.That(TestShell.Configuration.ViewConfig.LastOpenedProjectFile.Value, Is.EqualTo(second));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Returning_to_a_project_restores_its_edits_tabs_selection_and_history()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("returning", extraScene: true);
            string second = await CreateClosedProjectAsync("elsewhere");
            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();
            Project project = TestShell.Project.CurrentProject.Value!;
            Scene edited = project.Items.OfType<Scene>().Single(s => Path.GetFileName(s.Uri!.LocalPath) == "extra.scene");
            TestShell.Editor.ActivateTabItem(edited);
            HeadlessTestHelpers.Settle();
            EditorTabItem selectedTab = TestShell.Editor.SelectedTabItem.Value!;
            var editor = (EditViewModel)selectedTab.Context.Value;
            EditorTabItem[] tabs = [.. TestShell.Editor.TabItems];
            MediaTime originalDuration = edited.Duration.Value;
            edited.Duration.Value = MediaTime.FromSeconds(73);
            editor.HistoryManager.Commit();
            Assert.That(editor.HistoryManager.CanUndo, Is.True);

            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();
            Assert.That(TestShell.Editor.TabItems, Has.None.SameAs(selectedTab));

            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project), "The model is reused, not read again.");
                Assert.That(TestShell.Editor.TabItems, Is.EqualTo(tabs), "The same tabs come back.");
                Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(selectedTab));
                Assert.That(selectedTab.Context.Value, Is.SameAs(editor));
                Assert.That(editor.IsDisposingOrDisposed, Is.False);
                Assert.That(edited.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(73)));
                Assert.That(editor.HasUnsavedSceneChanges, Is.True, "Switching away saved nothing.");
                Assert.That(editor.HistoryManager.CanUndo, Is.True);
                Assert.That(TestShell.Editor.HasInactiveTabs(project), Is.False);
            });

            editor.HistoryManager.Undo();
            Assert.That(edited.Duration.Value, Is.EqualTo(originalDuration), "The undo history survived the switch.");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Reopening_a_loaded_file_activates_its_session_instead_of_reading_the_file()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("loaded");
            string second = await CreateClosedProjectAsync("other");
            await TestShell.Project.OpenProject(first);
            Project project = TestShell.Project.CurrentProject.Value!;
            ProjectSession session = Registry.ActiveSession.Value!;
            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();
            // Reading the file again would now fail; activating the loaded session does not read it.
            File.WriteAllText(first, "{ invalid json");

            ProjectSession? reused = await TestShell.Project.EnsureSessionAsync(first);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(reused, Is.SameAs(session));
                Assert.That(Registry.ActiveSession.Value, Is.SameAs(session));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(Registry.Sessions, Has.Count.EqualTo(2));
            });

            // Selecting the project that is active already changes nothing either.
            Assert.That(await TestShell.Project.EnsureSessionAsync(first), Is.SameAs(session));
            Assert.That(Registry.Sessions, Has.Count.EqualTo(2));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_a_session_unloads_only_that_session()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("kept");
            string second = await CreateClosedProjectAsync("closed");
            await TestShell.Project.OpenProject(first);
            ProjectSession kept = Registry.ActiveSession.Value!;
            await TestShell.Project.OpenProject(second);
            ProjectSession closed = Registry.ActiveSession.Value!;
            HeadlessTestHelpers.Settle();

            await TestShell.Project.CloseProjectAsync();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(Registry.Sessions, Is.EqualTo(new[] { kept }));
                Assert.That(Registry.ActiveSession.Value, Is.Null);
                Assert.That(closed.Project.HierarchicalParent, Is.Null);
                Assert.That(kept.Project.HierarchicalParent, Is.SameAs(BeutlApplication.Current));
                Assert.That(TestShell.Editor.TabItems, Is.Empty);
                Assert.That(TestShell.Editor.HasInactiveTabs(kept.Project), Is.True);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_an_inactive_session_saves_and_disposes_its_tabs_and_leaves_the_active_one_alone()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("background");
            string second = await CreateClosedProjectAsync("foreground");
            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();
            Project background = TestShell.Project.CurrentProject.Value!;
            Scene scene = background.Items.OfType<Scene>().Single();
            var backgroundEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            scene.Duration.Value = MediaTime.FromSeconds(41);
            backgroundEditor.HistoryManager.Commit();
            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();
            ProjectSession foreground = Registry.ActiveSession.Value!;
            EditorTabItem[] foregroundTabs = [.. TestShell.Editor.TabItems];
            var commits = new List<ProjectService.ProjectTransitionCommit>();
            Action<ProjectService.ProjectTransitionCommit> record = commits.Add;
            TestShell.Project.TransitionCommitted += record;
            try
            {
                bool closed = await TestShell.Project.TryCloseProjectAsync(
                    background, ProjectService.ProjectCloseIntent.SaveChanges);
                HeadlessTestHelpers.Settle();

                Assert.Multiple(() =>
                {
                    Assert.That(closed, Is.True);
                    Assert.That(Registry.Sessions, Is.EqualTo(new[] { foreground }));
                    Assert.That(Registry.ActiveSession.Value, Is.SameAs(foreground));
                    Assert.That(TestShell.Editor.TabItems, Is.EqualTo(foregroundTabs));
                    Assert.That(TestShell.Editor.HasInactiveTabs(background), Is.False);
                    Assert.That(backgroundEditor.IsDisposingOrDisposed, Is.True);
                    Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration.Value,
                        Is.EqualTo(MediaTime.FromSeconds(41)), "The edits were saved before the tabs were disposed.");
                    Assert.That(commits.Select(c => (c.Change, c.ActiveSessionChanged, c.ActiveProject)),
                        Is.EqualTo(new[] { (ProjectService.ProjectSessionChange.Closed, false, (Project?)foreground.Project) }));
                });
            }
            finally
            {
                TestShell.Project.TransitionCommitted -= record;
            }
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_switch_publishes_a_deactivation_and_an_activation()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("from");
            string second = await CreateClosedProjectAsync("to");
            await TestShell.Project.OpenProject(first);
            ProjectSession from = Registry.ActiveSession.Value!;
            var commits = new List<ProjectService.ProjectTransitionCommit>();
            var closeKinds = new List<ProjectService.ProjectCloseKind>();
            Action<ProjectService.ProjectTransitionCommit> record = commits.Add;
            Func<ProjectService.ProjectCloseContext, CancellationToken, Task> observe = (context, _) =>
            {
                closeKinds.Add(context.Kind);
                Assert.That(context.Session, Is.SameAs(from));
                return Task.CompletedTask;
            };
            TestShell.Project.TransitionCommitted += record;
            TestShell.Project.ClosingPreparing += observe;
            try
            {
                await TestShell.Project.OpenProject(second);
                HeadlessTestHelpers.Settle();
            }
            finally
            {
                TestShell.Project.TransitionCommitted -= record;
                TestShell.Project.ClosingPreparing -= observe;
            }

            ProjectSession to = Registry.ActiveSession.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(closeKinds, Is.EqualTo(new[] { ProjectService.ProjectCloseKind.Deactivate }));
                Assert.That(commits.Select(c => (c.Change, c.Session)), Is.EqualTo(new[]
                {
                    (ProjectService.ProjectSessionChange.Deactivated, from),
                    (ProjectService.ProjectSessionChange.Activated, to),
                }));
                Assert.That(commits[0].ActiveProject, Is.Null);
                Assert.That(commits[1].ActiveProject, Is.SameAs(to.Project));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_failed_activation_of_a_loaded_session_restores_the_previous_one_and_keeps_both_loaded()
    {
        await TestReset.ResetShellAsync();
        Func<Project, Task>? reject = null;
        try
        {
            string first = await CreateClosedProjectAsync("rejected");
            string second = await CreateClosedProjectAsync("current");
            await TestShell.Project.OpenProject(first);
            ProjectSession rejected = Registry.ActiveSession.Value!;
            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();
            ProjectSession current = Registry.ActiveSession.Value!;
            EditorTabItem[] currentTabs = [.. TestShell.Editor.TabItems];
            EditorTabItem currentSelection = TestShell.Editor.SelectedTabItem.Value!;
            reject = project => ReferenceEquals(project, rejected.Project)
                ? throw new IOException("The session cannot be activated.")
                : Task.CompletedTask;
            TestShell.Project.Opened += reject;

            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(Registry.ActiveSession.Value, Is.SameAs(current));
                Assert.That(Registry.Sessions, Is.EqualTo(new[] { rejected, current }),
                    "A session that was loaded before stays loaded.");
                Assert.That(TestShell.Editor.TabItems, Is.EqualTo(currentTabs));
                Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(currentSelection));
                Assert.That(TestShell.Editor.HasInactiveTabs(rejected.Project), Is.True,
                    "The rolled-back session keeps its tabs aside.");
                Assert.That(TestShell.Configuration.ViewConfig.LastOpenedProjectFile.Value, Is.EqualTo(second));
            });
        }
        finally
        {
            if (reject is not null) TestShell.Project.Opened -= reject;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_vetoed_deactivation_keeps_the_active_session_and_loads_nothing()
    {
        await TestReset.ResetShellAsync();
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> veto = (context, _) =>
            context.Kind == ProjectService.ProjectCloseKind.Deactivate
                ? throw new ProjectCloseAbortedException("Vetoed.")
                : Task.CompletedTask;
        try
        {
            string first = await CreateClosedProjectAsync("vetoing");
            string second = await CreateClosedProjectAsync("unreached");
            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();
            ProjectSession active = Registry.ActiveSession.Value!;
            EditorTabItem[] tabs = [.. TestShell.Editor.TabItems];
            TestShell.Project.ClosingPreparing += veto;

            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(Registry.ActiveSession.Value, Is.SameAs(active));
                Assert.That(Registry.Sessions, Is.EqualTo(new[] { active }));
                Assert.That(TestShell.Editor.TabItems, Is.EqualTo(tabs));
            });
        }
        finally
        {
            TestShell.Project.ClosingPreparing -= veto;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Overlapping_opens_of_two_files_load_both_and_activate_the_newer()
    {
        await TestReset.ResetShellAsync();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? holdFile = null;
        Func<string, Task> hold = file =>
        {
            if (file != holdFile) return Task.CompletedTask;
            held.TrySetResult();
            return release.Task;
        };
        try
        {
            string current = await CreateClosedProjectAsync("running");
            string older = await CreateClosedProjectAsync("older");
            string newer = await CreateClosedProjectAsync("newer");
            await TestShell.Project.OpenProject(current);
            holdFile = current;
            TestShell.Project.Opening += hold;
            // Holds the transition so both requests below wait for it.
            Task running = TestShell.Project.OpenProject(current);
            await held.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<ProjectSession?> olderOpen = TestShell.Project.EnsureSessionAsync(older);
            Task<ProjectSession?> newerOpen = TestShell.Project.EnsureSessionAsync(newer);
            release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(10));
            ProjectSession? olderSession = await olderOpen.WaitAsync(TimeSpan.FromSeconds(10));
            ProjectSession? newerSession = await newerOpen.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(olderSession, Is.Not.Null, "The newer request did not cancel the older one.");
                Assert.That(newerSession, Is.Not.Null);
                Assert.That(Registry.Sessions.Select(s => s.FilePath), Is.EquivalentTo(new[] { current, older, newer }));
                Assert.That(Registry.ActiveSession.Value, Is.SameAs(newerSession), "The newest request wins the activation.");
                Assert.That(TestShell.Editor.TabItems.Select(t => t.Context.Value.Object),
                    Is.EqualTo(newerSession!.Project.Items));
            });
        }
        finally
        {
            release.TrySetResult();
            TestShell.Project.Opening -= hold;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Overlapping_opens_of_one_file_load_it_once()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string file = await CreateClosedProjectAsync("twice");

            Task<ProjectSession?> first = TestShell.Project.EnsureSessionAsync(file);
            Task<ProjectSession?> second = TestShell.Project.EnsureSessionAsync(file);
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            ProjectSession session = Registry.Sessions.Single();
            Assert.Multiple(() =>
            {
                Assert.That(Registry.ActiveSession.Value, Is.SameAs(session));
                Assert.That(second.Result, Is.SameAs(session));
                Assert.That(first.Result, Is.Null.Or.SameAs(session));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Closing_every_project_saves_and_unloads_the_background_sessions_too()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("all-first");
            string second = await CreateClosedProjectAsync("all-second");
            await TestShell.Project.OpenProject(first);
            HeadlessTestHelpers.Settle();
            Project background = TestShell.Project.CurrentProject.Value!;
            Scene scene = background.Items.OfType<Scene>().Single();
            scene.Duration.Value = MediaTime.FromSeconds(29);
            ((EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value).HistoryManager.Commit();
            await TestShell.Project.OpenProject(second);
            HeadlessTestHelpers.Settle();

            await TestShell.Project.CloseAllProjectsAsync();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(Registry.Sessions, Is.Empty);
                Assert.That(BeutlApplication.Current.Projects, Is.Empty);
                Assert.That(TestShell.Editor.TabItems, Is.Empty);
                Assert.That(TestShell.Editor.InactiveTabItems, Is.Empty);
                Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration.Value,
                    Is.EqualTo(MediaTime.FromSeconds(29)));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Creating_a_project_keeps_the_previous_one_loaded()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string first = await CreateClosedProjectAsync("before-create");
            await TestShell.Project.OpenProject(first);
            ProjectSession previous = Registry.ActiveSession.Value!;

            Project? created = await TestShell.Project.CreateProject(
                320, 180, new Rational(30), 44100, "created", TestShell.NewWorkspace("created"));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.Not.Null);
                Assert.That(Registry.ActiveSession.Value!.Project, Is.SameAs(created));
                Assert.That(Registry.Sessions, Has.Count.EqualTo(2));
                Assert.That(Registry.Sessions, Does.Contain(previous));
            });

            // A project cannot be created over the files of one that is loaded.
            Project? duplicate = await TestShell.Project.CreateProject(
                320, 180, new Rational(30), 44100, "before-create", Path.GetDirectoryName(Path.GetDirectoryName(first))!);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(duplicate, Is.Null);
                Assert.That(Registry.ActiveSession.Value!.Project, Is.SameAs(created));
                Assert.That(Registry.Sessions, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    // Writes a project to disk and leaves no session loaded for it.
    private static async Task<string> CreateClosedProjectAsync(string name, bool extraScene = false)
    {
        Project project = (await TestShell.Project.CreateProject(
            320, 180, new Rational(30), 44100, name, TestShell.NewWorkspace($"session-activation-{name}")))!;
        if (extraScene)
        {
            Scene scene = project.Items.OfType<Scene>().Single();
            var extra = new Scene(320, 180, "extra")
            {
                Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "extra.scene")),
            };
            CoreSerializer.StoreToUri(extra, extra.Uri);
            project.Items.Add(extra);
            CoreSerializer.StoreToUri(project, project.Uri!);
        }

        HeadlessTestHelpers.Settle();
        string file = project.Uri!.LocalPath;
        Assert.That(await TestShell.Project.TryCloseProjectAsync(project, ProjectService.ProjectCloseIntent.SaveChanges), Is.True);
        HeadlessTestHelpers.Settle();
        return file;
    }
}
