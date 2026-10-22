using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.ExtensionsPages;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.GitTestSupport;
using static Beutl.Testing.Headless.HeadlessTestHelpers;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VersionControlSaveTests
{
    [AvaloniaTest]
    public async Task Save_all_does_not_snapshot_partially_saved_files()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            "version-control-partial-save",
            "partial-save");

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        int commitsBeforeSave = await CountCommitsAsync(git.GitPath, projectRoot);
        var failedCommands = new FailedSaveOperation();
        var failedItem = new Scene
        {
            Uri = new Uri(Path.Combine(projectRoot, "failed.scene")),
        };
        TestShell.Editor.TabItems.Add(new EditorTabItem(
            new FailedSaveEditorContext(failedItem, failedCommands.SaveAsync)));
        project.Variables["partially-saved"] = "true";

        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

        int commitsAfterSave = await CountCommitsAsync(git.GitPath, projectRoot);
        int saveSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "save");
        WorkspaceStatus status = await TestShell.VersionControl.CurrentService!
            .GetStatusAsync(CancellationToken.None);
        Project persisted = CoreSerializer.RestoreFromUri<Project>(project.Uri);
        Assert.Multiple(() =>
        {
            Assert.That(failedCommands.SaveCalls, Is.EqualTo(1));
            Assert.That(persisted.Variables["partially-saved"], Is.EqualTo("true"));
            Assert.That(commitsAfterSave, Is.EqualTo(commitsBeforeSave));
            Assert.That(saveSnapshots, Is.Zero);
            Assert.That(status.IsClean, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Opening_and_auto_save_hash_updates_leave_pending_files_uncommitted()
    {
        bool oldAutoSave = EditViewModel.IsAutoSaveSuppressedForTesting;
        try
        {
            // Inside the try, so the shell resets before auto-save is back on.
            await using RealGitScope git = await RealGitScope.StartAsync(
                autoCommitOnSave: true,
                autoCommitOnClose: false,
                useLfsWhenAvailable: false);
            EditViewModel.IsAutoSaveSuppressedForTesting = false;
            Project project = await git.CreateProjectAsync(
                "version-control-background-media",
                "tracked",
                initialize: false);
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            Scene scene = project.Items.OfType<Scene>().Single();
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
            void AddMedia(string name)
            {
                string media = Path.Combine(projectRoot, name + ".png"); File.WriteAllBytes(media, png);
                var source = new Beutl.Media.Source.ImageSource(); source.ReadFrom(new Uri(media));
                var element = new Element { Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, name + ".belm")) };
                element.Objects.Add(new Beutl.Graphics.SourceImage { Source = { Value = source } });
                scene.Children.Add(element);
            }
            AddMedia("old");
            CoreSerializer.StoreToUri(scene, scene.Uri!);
            Assert.That(await GitTestSupport.InitializeCurrentProjectAsync(project,
                _ => Task.FromResult<GitIdentity?>(new GitIdentity("Beutl Headless Test", "headless@example.invalid"))), Is.True);
            int initialCommits = await CountCommitsAsync(git.GitPath, projectRoot);
            File.WriteAllText(Path.Combine(projectRoot, "pending.txt"), "Work the user has not committed.");
            TestShell.Editor.ActivateTabItem(scene);
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            await editor.WaitForMediaFingerprintsAsync();
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).MediaFingerprints, Has.Count.EqualTo(1));
            Assert.That(await CountCommitsAsync(git.GitPath, projectRoot), Is.EqualTo(initialCommits));

            AddMedia("new"); editor.HistoryManager.Commit("Add media");
            HeadlessTestHelpers.Settle(); await editor.WaitForMediaFingerprintsAsync();
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).MediaFingerprints, Has.Count.EqualTo(2));
            Assert.That(await CountCommitsAsync(git.GitPath, projectRoot), Is.EqualTo(initialCommits));
            Assert.That(await CountSnapshotsAsync(git.GitPath, projectRoot, "save"), Is.Zero);
            Assert.That(await RunGitAsync(git.GitPath, projectRoot, "ls-tree", "-r", "--name-only", "HEAD"), Does.Not.Contain("pending.txt"));
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = oldAutoSave;
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Delayed_media_fingerprints_are_recorded_by_the_next_save(bool saveAll)
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);
        var release = new TaskCompletionSource();
        try
        {
            Project project = await git.CreateProjectAsync(
                $"version-control-delayed-media-{saveAll}",
                "tracked");
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            await editor.WaitForMediaFingerprintsAsync();
            string media = Path.Combine(projectRoot, "image.png");
            File.WriteAllBytes(media, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));
            using (editor.HistoryManager.SuppressRecording())
            {
                var element = new Element { Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "media.belm")) };
                var source = new Beutl.Media.Source.ImageSource(); source.ReadFrom(new Uri(media));
                element.Objects.Add(new Beutl.Graphics.SourceImage { Source = { Value = source } });
                scene.Children.Add(element);
            }
            var entered = new TaskCompletionSource();
            editor.CaptureMediaFingerprints = async (value, token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                await new Beutl.Editor.MissingMediaService().UpdateFingerprintsAsync(value, token);
            };
            if (saveAll) await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
            else await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(await CountSnapshotsAsync(git.GitPath, projectRoot, "save"), Is.EqualTo(1));
            // An ordinary auto-save must not cancel the explicit save that is
            // waiting for its media hash, nor request a snapshot of its own.
            scene.Duration += TimeSpan.FromSeconds(1);
            editor.HistoryManager.Commit("Edit while hashing");
            HeadlessTestHelpers.Settle();
            release.SetResult();
            await editor.WaitForMediaFingerprintsAsync();
            // The save wrote the scene file before the media were hashed, so it references no fingerprint file,
            // and the update writes none and asks for no snapshot. The scene keeps the fingerprints instead.
            string hash = scene.MediaFingerprints[new Uri(media).AbsoluteUri].Sha256;
            Assert.That(File.Exists(MediaFingerprintFile.GetPath(scene.Uri!.LocalPath)), Is.False);
            Assert.That(await CountSnapshotsAsync(git.GitPath, projectRoot, "save"), Is.EqualTo(1));

            // The next save writes them, and its snapshot records them.
            if (saveAll) await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
            else await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            await editor.WaitForMediaFingerprintsAsync();
            string relativeScene = Path.GetRelativePath(projectRoot, scene.Uri.LocalPath).Replace('\\', '/');
            string committedScene = await RunGitAsync(git.GitPath, projectRoot, "show", $"HEAD:{relativeScene}");
            string relativeFingerprints = Path.GetRelativePath(projectRoot, MediaFingerprintFile.GetPath(scene.Uri.LocalPath))
                .Replace('\\', '/');
            string committedFingerprints = await RunGitAsync(git.GitPath, projectRoot, "show", $"HEAD:{relativeFingerprints}");
            Assert.That(committedFingerprints, Does.Contain(hash));
            Assert.That(committedScene, Does.Contain("\"MediaFingerprintFile\": \"media-fingerprints.json\""));
            Assert.That(committedScene, Does.Not.Contain(nameof(Scene.MediaFingerprints)));
            Assert.That(await CountSnapshotsAsync(git.GitPath, projectRoot, "save"), Is.EqualTo(2));
            Assert.That((await TestShell.VersionControl.CurrentService!.GetStatusAsync(CancellationToken.None)).IsClean, Is.True);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task Saving_after_only_a_media_timestamp_changed_leaves_the_scene_and_history_alone()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);
        Project project = await git.CreateProjectAsync(
            "version-control-media-timestamp",
            "tracked");
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        await editor.WaitForMediaFingerprintsAsync();
        string media = Path.Combine(projectRoot, "image.png");
        File.WriteAllBytes(media, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = new Element { Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "media.belm")) };
            var source = new Beutl.Media.Source.ImageSource(); source.ReadFrom(new Uri(media));
            element.Objects.Add(new Beutl.Graphics.SourceImage { Source = { Value = source } });
            scene.Children.Add(element);
        }
        // Hash the media before the first save, which then writes the fingerprints with the scene.
        editor.ScheduleMediaFingerprints();
        await editor.WaitForMediaFingerprintsAsync();
        await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
        await editor.WaitForMediaFingerprintsAsync();
        Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri).MediaFingerprints, Has.Count.EqualTo(1));
        Assert.That((await TestShell.VersionControl.CurrentService!.GetStatusAsync(CancellationToken.None)).IsClean, Is.True);
        // The fingerprints are committed in their own file next to the scene, not in the scene file.
        string fingerprintFile = MediaFingerprintFile.GetPath(scene.Uri.LocalPath);
        string relativeFingerprints = Path.GetRelativePath(projectRoot, fingerprintFile).Replace('\\', '/');
        Assert.That(
            (await RunGitAsync(git.GitPath, projectRoot, "ls-tree", "-r", "--name-only", "HEAD")).Split('\n'),
            Does.Contain(relativeFingerprints));
        Assert.That(File.ReadAllText(scene.Uri.LocalPath), Does.Not.Contain(nameof(Scene.MediaFingerprints)));
        int snapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "save");
        byte[] savedScene = File.ReadAllBytes(scene.Uri.LocalPath);
        byte[] savedFingerprints = File.ReadAllBytes(fingerprintFile);
        DateTime fingerprintsWritten = File.GetLastWriteTimeUtc(fingerprintFile);

        // As after a checkout on another machine: the same file with another timestamp.
        File.SetLastWriteTimeUtc(media, File.GetLastWriteTimeUtc(media).AddHours(-3));
        await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
        await editor.WaitForMediaFingerprintsAsync();

        int snapshotsAfter = await CountSnapshotsAsync(git.GitPath, projectRoot, "save");
        bool clean = (await TestShell.VersionControl.CurrentService!.GetStatusAsync(CancellationToken.None)).IsClean;
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(savedScene));
            Assert.That(File.ReadAllBytes(fingerprintFile), Is.EqualTo(savedFingerprints));
            Assert.That(File.GetLastWriteTimeUtc(fingerprintFile), Is.EqualTo(fingerprintsWritten), "The fingerprint file is not rewritten.");
            Assert.That(snapshotsAfter, Is.EqualTo(snapshots));
            Assert.That(clean, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Explicit_save_persists_element_edits_when_auto_save_is_disabled()
    {
        await TestReset.ResetShellAsync();
        bool oldAutoSave = EditViewModel.IsAutoSaveSuppressedForTesting;
        try
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = true;
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "explicit-element-save");
            Directory.CreateDirectory(location);
            Project project = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "explicit-save",
                location))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            await AddRectangleAsync(adder);
            HeadlessTestHelpers.Settle();
            Element element = scene.Children.Single();
            element.Name = "saved-without-auto-save";

            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();

            Element persisted = CoreSerializer.RestoreFromUri<Element>(element.Uri!);
            Assert.That(persisted.Name, Is.EqualTo("saved-without-auto-save"));
        }
        finally
        {
            try
            {
                await TestReset.ResetShellAsync();
            }
            finally
            {
                EditViewModel.IsAutoSaveSuppressedForTesting = oldAutoSave;
            }
        }
    }

    [AvaloniaTest]
    public async Task Explicit_save_creates_one_snapshot_and_a_second_clean_save_creates_none()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync("version-control-save", "tracked");

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        Assert.That(await CountCommitsAsync(git.GitPath, projectRoot), Is.EqualTo(1));

        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        Assert.That(
            editor.GetService(typeof(IProjectVersionControlService)),
            Is.SameAs(TestShell.VersionControl.CurrentService));

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await AddRectangleAsync(adder);
        HeadlessTestHelpers.Settle();

        await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
        int afterFirstSave = await CountCommitsAsync(git.GitPath, projectRoot);
        int saveSnapshotsAfterFirstSave = await CountSnapshotsAsync(git.GitPath, projectRoot, "save");

        await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
        int afterSecondSave = await CountCommitsAsync(git.GitPath, projectRoot);
        int saveSnapshotsAfterSecondSave = await CountSnapshotsAsync(git.GitPath, projectRoot, "save");

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstSave, Is.EqualTo(2));
            Assert.That(saveSnapshotsAfterFirstSave, Is.EqualTo(1));
            Assert.That(afterSecondSave, Is.EqualTo(2));
            Assert.That(saveSnapshotsAfterSecondSave, Is.EqualTo(1));
        });

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "close-marker.txt"), "close\n");
        await TestShell.MainViewModel.MenuBar.CloseProject.ExecuteAsync();
        int afterClose = await CountCommitsAsync(git.GitPath, projectRoot);
        int closeSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "close");

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(afterClose, Is.EqualTo(3));
            Assert.That(closeSnapshots, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Explicit_save_reports_snapshot_failure_without_reporting_file_save_failure(
        bool saveAll)
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            $"version-control-save-snapshot-warning-{saveAll}",
            "snapshot-warning");

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await AddRectangleAsync(adder);
        HeadlessTestHelpers.Settle();

        string hookPath = Path.Combine(projectRoot, ".git", "hooks", "pre-commit");
        // A rejecting hook explains itself on stderr; the warning has to pass that on.
        const string hookReason = "pre-commit: simulated lint rejection";
        await File.WriteAllTextAsync(
            hookPath,
            $"#!/bin/sh\necho '{hookReason}' >&2\nexit 17\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hookPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        using var notifications = new NotificationCapture();
        if (saveAll)
        {
            await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        }
        else
        {
            await TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
        }

        HeadlessTestHelpers.Settle();
        Notification[] observed = notifications.All.ToArray();
        string savedScene = await File.ReadAllTextAsync(scene.Uri!.LocalPath);
        int commitCount = await CountCommitsAsync(git.GitPath, projectRoot);
        Assert.Multiple(() =>
        {
            Assert.That(savedScene, Does.Contain(".belm"));
            Assert.That(
                observed.Count(item => item.Type == NotificationType.Success),
                Is.EqualTo(1));
            Assert.That(
                observed.Count(item => item.Type == NotificationType.Warning),
                Is.EqualTo(1));
            Assert.That(
                observed.Count(item =>
                    item.Type == NotificationType.Warning
                    && item.Message.Contains(hookReason, StringComparison.Ordinal)),
                Is.EqualTo(1),
                "The snapshot warning must carry the hook's reason.");
            Assert.That(
                observed.Any(item => item.Type == NotificationType.Error),
                Is.False);
            Assert.That(
                observed.Any(item => item.Message == MessageStrings.OperationFailed),
                Is.False);
            Assert.That(commitCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task Initialization_publishes_tracked_state_and_disables_the_enable_command()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(useLfsWhenAvailable: false);

        GitAvailability availability =
            await TestShell.VersionControl.GetAvailabilityAsync();
        Assert.That(
            availability.State,
            Is.EqualTo(GitAvailabilityState.Installed));

        await git.CreateProjectAsync(
            "version-control-tracked-state",
            "tracked-state",
            initialize: false);

        var enableCommand =
            (System.Windows.Input.ICommand)TestShell.MainViewModel.MenuBar.EnableVersionControl;
        Assert.Multiple(() =>
        {
            Assert.That(TestShell.VersionControl.IsTracked.Value, Is.False);
            Assert.That(enableCommand.CanExecute(null), Is.True);
        });

        bool initialized = await GitTestSupport.InitializeCurrentProjectAsync(
            TestShell.Project.CurrentProject.Value!,
            _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                "Beutl Headless Test",
                "headless@example.invalid")));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(initialized, Is.True);
            Assert.That(TestShell.VersionControl.IsTracked.Value, Is.True);
            Assert.That(enableCommand.CanExecute(null), Is.False);
            Assert.That(
                ((System.Windows.Input.ICommand)TestShell.MainViewModel.MenuBar.CommitVersion)
                .CanExecute(null),
                Is.True);
        });
    }

    [AvaloniaTest]
    public async Task New_project_dialog_opens_the_editor_after_git_records_the_first_version()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);
        var editorStates = new List<(ProjectLifecycleActivity Activity, bool Enabled)>();
        var disabledEditors = new List<ProjectLifecycleActivity>();
        var subscriptions = new List<IDisposable>();
        System.Collections.Specialized.NotifyCollectionChangedEventHandler onTabsChanged = (_, e) =>
        {
            foreach (EditorTabItem item in e.NewItems?.OfType<EditorTabItem>() ?? [])
            {
                IEditorContext context = item.Context.Value;
                editorStates.Add((TestShell.Editor.LifecycleActivity.Value, context.IsEnabled.Value));
                subscriptions.Add(context.IsEnabled.Subscribe(enabled =>
                {
                    if (!enabled)
                    {
                        disabledEditors.Add(TestShell.Editor.LifecycleActivity.Value);
                    }
                }));
            }
        };

        try
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)TestShell.Editor.TabItems)
                .CollectionChanged += onTabsChanged;
            string location = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-new-project-dialog");
            Directory.CreateDirectory(location);
            int identityRequests = 0;
            TestShell.VersionControl.RequestIdentityAsync = _ =>
            {
                identityRequests++;
                return Task.FromResult<GitIdentity?>(new GitIdentity(
                    "Beutl Headless Test",
                    "headless@example.invalid"));
            };
            var viewModel = new CreateNewProjectViewModel(
                TestShell.Project,
                TestShell.VersionControl);
            await WaitUntilAsync(() => viewModel.IsGitAvailable.Value);
            viewModel.Location.Value = location;
            viewModel.Name.Value = "dialog-tracked";
            viewModel.TrackHistory.Value = true;
            HeadlessTestHelpers.Settle();

            await viewModel.Create.ExecuteAsync();
            HeadlessTestHelpers.Settle();

            Project project = TestShell.Project.CurrentProject.Value!;
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            string? repositoryRoot = TestShell.VersionControl.CurrentService?.Repository?.ProjectRoot;
            int commits = await CountCommitsAsync(git.GitPath, projectRoot);
            string status = await RunGitAsync(git.GitPath, projectRoot, "status", "--porcelain");
            Assert.Multiple(() =>
            {
                Assert.That(identityRequests, Is.EqualTo(1));
                Assert.That(commits, Is.EqualTo(1));
                Assert.That(status.Trim(), Is.Empty);
                Assert.That(TestShell.VersionControl.IsTracked.Value, Is.True);
                Assert.That(
                    repositoryRoot is not null && FilePathComparison.AreSameCanonicalPath(repositoryRoot, projectRoot),
                    Is.True, $"Repository root '{repositoryRoot}' should match project root '{projectRoot}'.");
                // The editor opens once, behind the progress view, and is never disabled for Git.
                Assert.That(
                    editorStates,
                    Is.EqualTo(new[] { (ProjectLifecycleActivity.CreatingProject, true) }));
                Assert.That(disabledEditors, Is.Empty);
                Assert.That(TestShell.Editor.TabItems.Single().Context.Value.IsEnabled.Value, Is.True);
                Assert.That(
                    TestShell.Editor.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.None));
            });

            await TestShell.MainViewModel.MenuBar.CloseProject.ExecuteAsync();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
                // Closing disables the editor only behind the progress view.
                Assert.That(disabledEditors, Is.Not.Empty);
                Assert.That(disabledEditors, Has.All.EqualTo(ProjectLifecycleActivity.ClosingProject));
                Assert.That(
                    TestShell.Editor.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.None));
            });
        }
        finally
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)TestShell.Editor.TabItems)
                .CollectionChanged -= onTabsChanged;
            foreach (IDisposable subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    [AvaloniaTest]
    public async Task Enabling_version_control_disables_the_open_editor_only_behind_the_progress_view()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(useLfsWhenAvailable: false);
        var editorStates = new List<(bool Enabled, ProjectLifecycleActivity Activity)>();
        IDisposable? subscription = null;

        try
        {
            Project project = await git.CreateProjectAsync(
                "version-control-enable-progress",
                "enable-progress",
                initialize: false);
            IEditorContext editor = TestShell.Editor.TabItems.Single().Context.Value;
            subscription = editor.IsEnabled
                .Skip(1)
                .Subscribe(enabled => editorStates.Add((enabled, TestShell.Editor.LifecycleActivity.Value)));
            var identityRequests = new List<(bool EditorEnabled, ProjectLifecycleActivity Activity)>();

            bool initialized = await GitTestSupport.InitializeCurrentProjectAsync(
                project,
                _ =>
                {
                    identityRequests.Add((editor.IsEnabled.Value, TestShell.Editor.LifecycleActivity.Value));
                    return Task.FromResult<GitIdentity?>(new GitIdentity(
                        "Beutl Headless Test",
                        "headless@example.invalid"));
                });
            HeadlessTestHelpers.Settle();

            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            int commits = await CountCommitsAsync(git.GitPath, projectRoot);
            string status = await RunGitAsync(git.GitPath, projectRoot, "status", "--porcelain");
            Assert.Multiple(() =>
            {
                Assert.That(initialized, Is.True);
                Assert.That(commits, Is.EqualTo(1));
                Assert.That(status.Trim(), Is.Empty);
                Assert.That(TestShell.VersionControl.IsTracked.Value, Is.True);
                // The identity is asked for while the editor is usable and in view.
                Assert.That(
                    identityRequests,
                    Is.EqualTo(new[] { (true, ProjectLifecycleActivity.None) }));
                // Both attempts, before and after the identity prompt, disable the editor only behind the
                // progress view.
                Assert.That(
                    editorStates,
                    Is.EqualTo(new[]
                    {
                        (false, ProjectLifecycleActivity.EnablingVersionControl),
                        (true, ProjectLifecycleActivity.EnablingVersionControl),
                        (false, ProjectLifecycleActivity.EnablingVersionControl),
                        (true, ProjectLifecycleActivity.EnablingVersionControl),
                    }));
                Assert.That(editor.IsEnabled.Value, Is.True);
                Assert.That(
                    TestShell.Editor.LifecycleActivity.Value,
                    Is.EqualTo(ProjectLifecycleActivity.None));
            });
        }
        finally
        {
            subscription?.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Discovered_repository_hygiene_finishes_before_tracked_service_publication()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            "version-control-auto-hygiene",
            "auto-hygiene");

        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        int initialCommitCount = await CountCommitsAsync(git.GitPath, projectRoot);
        await TestShell.MainViewModel.MenuBar.CloseProject.ExecuteAsync();
        File.Delete(Path.Combine(projectRoot, ".gitignore"));
        File.Delete(Path.Combine(projectRoot, ".gitattributes"));

        await TestShell.Project.OpenProject(projectFile);
        await WaitUntilAsync(
            () => TestShell.VersionControl.CurrentService?.Repository is not null);
        string staged = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "diff",
            "--cached",
            "--name-only");
        int finalCommitCount = await CountCommitsAsync(git.GitPath, projectRoot);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".gitignore")),
                Is.EqualTo("**/.beutl/\n*.[tT][mM][pP]\n"));
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".gitattributes")),
                Does.Contain("*.[bB][eE][pP] text eol=lf\n"));
            Assert.That(finalCommitCount, Is.EqualTo(initialCommitCount));
            Assert.That(staged, Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task Identity_view_model_prefills_the_os_user_and_writes_the_repository_identity()
    {
        var viewModel = new GitIdentityDialogViewModel();

        Assert.That(viewModel.Name.Value, Is.EqualTo(Environment.UserName));
        viewModel.Email.Value = "local@example.invalid";
        GitIdentity identity = viewModel.CreateIdentity();

        Assert.That(
            identity,
            Is.EqualTo(new GitIdentity(Environment.UserName, "local@example.invalid")));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Save_reserves_the_workspace_while_an_extension_writes_files(bool saveAll)
    {
        await TestReset.ResetShellAsync();
        try
        {
            BlockingSaveOperation blocking = await CreateProjectWithBlockingEditorAsync(
                saveAll ? "save-all-workspace-lease" : "save-workspace-lease");

            Task save = saveAll
                ? TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync()
                : TestShell.MainViewModel.MenuBar.Save.ExecuteAsync();
            try
            {
                await WaitUntilAsync(() => blocking.SaveEntered, message: "The extension save never started.");

                using IDisposable? blockedMutation = TestShell.Editor.TryBeginWorktreeMutation();
                Assert.That(
                    blockedMutation,
                    Is.Null,
                    "A worktree mutation must not start while an extension is still writing files.");
            }
            finally
            {
                // A failed assertion must not leave the extension save parked: it holds the
                // process-wide write lease, which would wedge every later test in the assembly.
                blocking.Release();
            }

            // Awaiting an unbounded pending save would turn a lease deadlock into a hung CI job
            // instead of a failing test.
            await WaitUntilAsync(() => save.IsCompleted, message: "The save never completed.");
            await save;

            using IDisposable? mutationAfterSave = TestShell.Editor.TryBeginWorktreeMutation();
            Assert.That(
                mutationAfterSave,
                Is.Not.Null,
                "The write lease must be released once every save completed.");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static async Task<BlockingSaveOperation> CreateProjectWithBlockingEditorAsync(string directoryName)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, directoryName);
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(
            640,
            480,
            30,
            44100,
            directoryName,
            location))!;
        HeadlessTestHelpers.Settle();

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        var blocking = new BlockingSaveOperation();
        var blockingItem = new Scene
        {
            Uri = new Uri(Path.Combine(projectRoot, "blocking.scene")),
        };
        var tabItem = new EditorTabItem(new FailedSaveEditorContext(blockingItem, blocking.SaveAsync));
        TestShell.Editor.TabItems.Add(tabItem);
        TestShell.Editor.SelectedTabItem.Value = tabItem;
        HeadlessTestHelpers.Settle();
        return blocking;
    }

    private static string ToRepositoryPath(string repositoryRoot, string path)
    {
        return Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static async Task<int> CountCommitsAsync(string gitPath, string repositoryRoot)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "rev-list", "--count", "HEAD");
        return int.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountSnapshotsAsync(string gitPath, string repositoryRoot, string kind)
    {
        string output = await RunGitAsync(gitPath, repositoryRoot, "log", "--format=%B");
        return CountOccurrences(output, $"Beutl-Snapshot: {kind}");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [AvaloniaTest]
    public async Task Initial_revision_records_editor_state_that_autosave_has_not_written()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            "version-control-initial-revision",
            "initial-revision",
            initialize: false);
        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;

        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await AddRectangleAsync(adder);
        HeadlessTestHelpers.Settle();

        bool initialized = await GitTestSupport.InitializeCurrentProjectAsync(
            TestShell.Project.CurrentProject.Value!,
            _ => Task.FromResult<GitIdentity?>(new GitIdentity(
                "Beutl Headless Test",
                "headless@example.invalid")));
        Assert.That(initialized, Is.True);

        WorkspaceStatus status = await TestShell.VersionControl.CurrentService!
            .GetStatusAsync(CancellationToken.None);
        string committedScene = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "show",
            $"HEAD:{ToRepositoryPath(projectRoot, scene.Uri!.LocalPath)}");
        int commits = await CountCommitsAsync(git.GitPath, projectRoot);

        Assert.Multiple(() =>
        {
            Assert.That(commits, Is.EqualTo(1));
            Assert.That(status.IsClean, Is.True);
            Assert.That(committedScene, Does.Contain(".belm"));
        });
    }

    [AvaloniaTest]
    public async Task Close_snapshot_records_editor_state_that_autosave_has_not_written()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            "version-control-close-persists",
            "close-persists");

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        Scene scene = project.Items.OfType<Scene>().Single();
        string scenePath = ToRepositoryPath(projectRoot, scene.Uri!.LocalPath);
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await AddRectangleAsync(adder);
        HeadlessTestHelpers.Settle();

        await TestShell.MainViewModel.MenuBar.CloseProject.ExecuteAsync();

        string committedScene = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "show",
            $"HEAD:{scenePath}");
        string statusOutput = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "status",
            "--porcelain");
        int closeSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "close");

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(closeSnapshots, Is.EqualTo(1));
            Assert.That(committedScene, Does.Contain(".belm"));
            Assert.That(statusOutput.Trim(), Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task Opening_another_project_reloads_shared_scenes_saved_on_close()
    {
        bool oldAutoSave = EditViewModel.IsAutoSaveSuppressedForTesting;

        try
        {
            // Inside the try, so the shell resets before auto-save is back on.
            await using RealGitScope git = await RealGitScope.StartAsync(
                autoCommitOnSave: false,
                autoCommitOnClose: true,
                useLfsWhenAvailable: false);
            Project original = await git.CreateProjectAsync(
                "shared-scene-on-close",
                "original",
                width: 320,
                height: 180);

            Scene scene = original.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            string alternate = Path.Combine(Path.GetDirectoryName(original.Uri!.LocalPath)!, "alternate.bep");
            File.Copy(original.Uri.LocalPath, alternate);

            EditViewModel.IsAutoSaveSuppressedForTesting = true;
            scene.Duration = TimeSpan.FromSeconds(73);
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration, Is.Not.EqualTo(scene.Duration));

            await TestShell.Project.OpenProject(alternate);
            Project opened = TestShell.Project.CurrentProject.Value!;
            Scene openedScene = opened.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(openedScene);
            HeadlessTestHelpers.Settle();
            Assert.That(await ((ISavableEditorContext)TestShell.Editor.SelectedTabItem.Value!.Context.Value).SaveAsync(), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(opened.Uri!.LocalPath, Is.EqualTo(alternate));
                Assert.That(openedScene.Uri, Is.EqualTo(scene.Uri));
                Assert.That(openedScene.Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
                Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration,
                    Is.EqualTo(TimeSpan.FromSeconds(73)), "Saving the newly opened project must retain the close-time edits.");
            });
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = oldAutoSave;
        }
    }

    [AvaloniaTest]
    public async Task Expected_project_close_does_not_close_a_queued_replacement_project()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;

        try
        {
            config.AutoCommitOnClose = false;
            string replacementLocation = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-expected-close-replacement");
            string expectedLocation = Path.Combine(
                BeutlHomeIsolation.CurrentHome!,
                "version-control-expected-close-current");
            Directory.CreateDirectory(replacementLocation);
            Directory.CreateDirectory(expectedLocation);
            Project replacement = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "replacement",
                replacementLocation))!;
            string replacementFile = replacement.Uri!.LocalPath;
            Project expected = (await TestShell.Project.CreateProject(
                640,
                480,
                30,
                44100,
                "expected",
                expectedLocation))!;

            ProjectService.ProjectTransitionScope transition
                = await TestShell.Project.BeginVersionControlTransitionAsync(
                    TestShell.VersionControl,
                    CancellationToken.None);
            Task<bool> queuedClose = TestShell.Project.TryCloseProjectAsync(
                expected,
                ProjectService.ProjectCloseIntent.SaveChanges);
            try
            {
                await transition.CloseProjectAsync();
                await transition.OpenProjectAsync(replacementFile);
                Assert.That(queuedClose.IsCompleted, Is.False);
            }
            finally
            {
                await transition.DisposeAsync();
            }

            bool closed = await queuedClose.WaitAsync(TimeSpan.FromSeconds(5));
            var packageHandler = new PackageOperationHandler(
                TestShell.MainViewModel._beutlClients,
                TestShell.Editor,
                TestShell.Project);
            bool primaryClosed = await packageHandler.HandleProjectCloseChoice(
                FAContentDialogResult.Primary,
                expected);
            bool secondaryClosed = await packageHandler.HandleProjectCloseChoice(
                FAContentDialogResult.Secondary,
                expected);
            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.False);
                Assert.That(primaryClosed, Is.False);
                Assert.That(secondaryClosed, Is.False);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Not.Null);
                Assert.That(
                    TestShell.Project.CurrentProject.Value!.Uri!.LocalPath,
                    Is.EqualTo(replacementFile));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
            config.AutoCommitOnClose = oldAutoCommitOnClose;
        }
    }

    [AvaloniaTest]
    public async Task Package_close_without_saving_discards_edits_and_skips_the_close_snapshot()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            "version-control-package-discard-close",
            "package-discard-close");

        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        int commitsBefore = await CountCommitsAsync(git.GitPath, projectRoot);
        project.Variables["package-discard"] = "must not be persisted";
        var packageHandler = new PackageOperationHandler(
            TestShell.MainViewModel._beutlClients,
            TestShell.Editor,
            TestShell.Project);

        bool closed = await packageHandler.HandleProjectCloseChoice(
            FAContentDialogResult.Primary,
            project);

        int commitsAfter = await CountCommitsAsync(git.GitPath, projectRoot);
        int closeSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "close");
        Project persisted = CoreSerializer.RestoreFromUri<Project>(new Uri(projectFile));
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(commitsAfter, Is.EqualTo(commitsBefore));
            Assert.That(closeSnapshots, Is.Zero);
            Assert.That(persisted.Variables.ContainsKey("package-discard"), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Package_close_without_saving_does_not_commit_edits_already_on_disk()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            "version-control-package-discard-autosaved-close",
            "package-discard-autosaved-close");

        string projectFile = project.Uri!.LocalPath;
        string projectRoot = Path.GetDirectoryName(projectFile)!;
        int commitsBefore = await CountCommitsAsync(git.GitPath, projectRoot);
        // Autosave writes an edit the moment it is made, so the worktree already differs from
        // HEAD when the user picks the close path that keeps nothing.
        project.Variables["package-discard"] = "autosaved before the close";
        CoreSerializer.StoreToUri(project, project.Uri, CoreSerializationMode.Write);
        var packageHandler = new PackageOperationHandler(
            TestShell.MainViewModel._beutlClients,
            TestShell.Editor,
            TestShell.Project);

        bool closed = await packageHandler.HandleProjectCloseChoice(
            FAContentDialogResult.Primary,
            project);

        int commitsAfter = await CountCommitsAsync(git.GitPath, projectRoot);
        int closeSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "close");
        string committedProject = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "show",
            $"HEAD:{Path.GetFileName(projectFile)}");
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(commitsAfter, Is.EqualTo(commitsBefore));
            Assert.That(closeSnapshots, Is.Zero);
            // The project file also names the scene after the project, so look for the value.
            Assert.That(committedProject, Does.Not.Contain("autosaved before the close"));
        });
    }

    [AvaloniaTest]
    public async Task Package_save_and_close_notifies_version_control_before_retirement()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: true,
            autoCommitOnClose: false,
            useLfsWhenAvailable: false);

        Project project = await git.CreateProjectAsync(
            "version-control-package-save-close",
            "package-save-close");

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        int commitsBefore = await CountCommitsAsync(git.GitPath, projectRoot);
        project.Variables["package-save"] = "persisted before close";
        var packageHandler = new PackageOperationHandler(
            TestShell.MainViewModel._beutlClients,
            TestShell.Editor,
            TestShell.Project);

        bool closed = await packageHandler.HandleProjectCloseChoice(
            FAContentDialogResult.Secondary,
            project);

        int commitsAfter = await CountCommitsAsync(git.GitPath, projectRoot);
        int saveSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "save");
        int closeSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "close");
        string committedProject = await RunGitAsync(
            git.GitPath,
            projectRoot,
            "show",
            $"HEAD:{Path.GetFileName(project.Uri.LocalPath)}");
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(commitsAfter, Is.EqualTo(commitsBefore + 1));
            Assert.That(saveSnapshots, Is.EqualTo(1));
            Assert.That(closeSnapshots, Is.Zero);
            Assert.That(committedProject, Does.Contain("package-save"));
        });
    }

    [AvaloniaTest]
    public async Task Window_close_is_cancelled_when_an_editor_cannot_save_the_close_snapshot()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false,
            autoCommitOnClose: true,
            useLfsWhenAvailable: false);
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;

        try
        {
            Project project = await git.CreateProjectAsync(
                "version-control-close-refused",
                "close-refused");

            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            int commitsBeforeClose = await CountCommitsAsync(git.GitPath, projectRoot);
            var failedCommands = new FailedSaveOperation();
            var failedItem = new Scene
            {
                Uri = new Uri(Path.Combine(projectRoot, "refuses-to-save.scene")),
            };
            var failedContext = new FailedSaveEditorContext(failedItem, failedCommands.SaveAsync);
            var failedTab = new EditorTabItem(failedContext);
            TestShell.Editor.TabItems.Add(failedTab);

            bool disposed = await TestShell.MainViewModel.TryDisposeForWindowCloseAsync();

            int closeSnapshots = await CountSnapshotsAsync(git.GitPath, projectRoot, "close");
            int commitsAfterClose = await CountCommitsAsync(git.GitPath, projectRoot);
            Assert.Multiple(() =>
            {
                Assert.That(disposed, Is.False);
                Assert.That(failedCommands.SaveCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(TestShell.Editor.TabItems, Does.Contain(failedTab));
                Assert.That(failedContext.IsEnabled.Value, Is.True);
                Assert.That(TestShell.VersionControl.CurrentService, Is.Not.Null);
                Assert.That(closeSnapshots, Is.Zero);
                Assert.That(commitsAfterClose, Is.EqualTo(commitsBeforeClose));
            });

            var packageHandler = new PackageOperationHandler(
                TestShell.MainViewModel._beutlClients,
                TestShell.Editor,
                TestShell.Project);
            config.AutoCommitOnClose = false;
            Assert.That(
                await packageHandler.HandleProjectCloseChoice(
                    FAContentDialogResult.Secondary,
                    project),
                Is.False);
            int commitsAfterFailedPackageSave = await CountCommitsAsync(git.GitPath, projectRoot);
            config.AutoCommitOnClose = true;
            Assert.Throws<ProjectCloseAbortedException>(TestShell.Project.CloseProjectOrThrow);
            Assert.Multiple(() =>
            {
                Assert.That(failedCommands.SaveCalls, Is.EqualTo(3));
                Assert.That(commitsAfterFailedPackageSave, Is.EqualTo(commitsBeforeClose));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(TestShell.Editor.TabItems, Does.Contain(failedTab));
            });
        }
        finally
        {
            // Cleared before the reset: the tab that refuses to save is still open, so the reset's
            // own close would be refused too and leave the project open for the next case.
            config.AutoCommitOnClose = false;
        }
    }

    [AvaloniaTest]
    public async Task Initialization_identity_prompt_resaves_edits_made_while_prompt_is_open()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(useLfsWhenAvailable: false);
        Project project = await git.CreateProjectAsync(
            "version-control-identity-freshness",
            "identity-freshness",
            initialize: false);
        var promptEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> initialization = GitTestSupport.InitializeCurrentProjectAsync(
            project,
            async _ =>
            {
                promptEntered.SetResult();
                await releasePrompt.Task;
                return new GitIdentity("Beutl Test", "beutl@example.invalid");
            });
        await promptEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        project.Variables["identity-race"] = "edited-during-prompt";
        releasePrompt.SetResult();
        Assert.That(await initialization, Is.True);

        string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string projectName = Path.GetFileName(project.Uri.LocalPath);
        string committed = await RunGitAsync(git.GitPath, projectRoot, "show", $"HEAD:{projectName}");
        Assert.That(committed, Does.Contain("edited-during-prompt"));
    }

    private static async Task AddRectangleAsync(IElementAdder adder)
    {
        ElementAddResult result = await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(1),
            Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))],
            CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Failure?.Message);
    }

    private sealed class FailedSaveEditorContext(
        CoreObject obj,
        Func<ValueTask<bool>> saveAsync) : ISavableEditorContext
    {
        public CoreObject Object { get; } = obj;

        public EditorExtension Extension => SceneEditorExtension.Instance;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public ValueTask<bool> SaveAsync() => saveAsync();

        public object? GetService(Type serviceType) => null;

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
        {
            return default;
        }

        public T? FindToolTab<T>()
            where T : IToolContext
        {
            return default;
        }

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }
    }

    private sealed class FailedSaveOperation
    {
        public int SaveCalls { get; private set; }

        public ValueTask<bool> SaveAsync()
        {
            SaveCalls++;
            return ValueTask.FromResult(false);
        }
    }

    private sealed class BlockingSaveOperation
    {
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SaveEntered { get; private set; }

        public async ValueTask<bool> SaveAsync()
        {
            SaveEntered = true;
            return await _release.Task;
        }

        public void Release() => _release.TrySetResult(true);
    }
}
