using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.Views;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// "Delete from Disk" in the start page's recent list: what it deletes, what it refuses to touch, and
// that it cannot interleave with a project transition.
[TestFixture]
public class ProjectDiskDeletionTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "disk-deletion", name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<(string ProjectFile, string SceneFile)> CreateClosedProjectAsync(
        string name,
        string location)
    {
        Project project = (await TestShell.Project.CreateProject(320, 180, 30, 44100, name, location))!;
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;
        string sceneFile = project.Items.OfType<Scene>().Single().Uri!.LocalPath;
        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();
        return (projectFile, sceneFile);
    }

    private static ProjectDiskDeletion CreateDeletion(
        List<FAContentDialog> shown,
        FAContentDialogResult answer = FAContentDialogResult.Primary)
    {
        return new ProjectDiskDeletion(TestShell.Project, TestShell.Editor)
        {
            ConfirmAsync = dialog =>
            {
                shown.Add(dialog);
                return Task.FromResult(answer);
            },
        };
    }

    private static string DialogText(FAContentDialog dialog)
    {
        return dialog.Content is Panel panel
            ? string.Join("\n", panel.Children.OfType<TextBlock>().Select(text => text.Text))
            : dialog.Content?.ToString() ?? string.Empty;
    }

    private static string CreateFile(string path, string contents = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [AvaloniaTest]
    public async Task Deletes_a_projects_own_folder_and_forgets_what_was_in_it()
    {
        await TestReset.ResetShellAsync();
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string workspace = NewWorkspace("own-folder");
        (string projectFile, string sceneFile) = await CreateClosedProjectAsync("doomed", workspace);
        string folder = Path.GetDirectoryName(projectFile)!;
        // Git keeps its objects read-only, which Windows refuses to delete as they are.
        string gitObject = CreateFile(Path.Combine(folder, ".git", "objects", "ab", "cdef"), "object");
        File.SetAttributes(gitObject, FileAttributes.ReadOnly);
        string render = CreateFile(Path.Combine(folder, "doomed", "doomed.mp4"));
        string unrelated = Path.Combine(workspace, "elsewhere", "elsewhere.scene");
        viewConfig.UpdateRecentFile(sceneFile);
        viewConfig.UpdateRecentFile(unrelated);
        var shown = new List<FAContentDialog>();
        try
        {
            await CreateDeletion(shown).DeleteAsync(projectFile);

            Assert.Multiple(() =>
            {
                Assert.That(shown, Has.Count.EqualTo(1));
                Assert.That(DialogText(shown[0]), Does.Contain(MessageStrings.ConfirmDeleteProjectFolderFromDisk));
                Assert.That(DialogText(shown[0]), Does.Contain(folder));
                Assert.That(shown[0].DefaultButton, Is.EqualTo(FAContentDialogButton.Close));
                Assert.That(Directory.Exists(folder), Is.False);
                Assert.That(File.Exists(render), Is.False);
                Assert.That(Directory.Exists(workspace), Is.True);
                Assert.That(viewConfig.RecentProjects, Does.Not.Contain(projectFile));
                Assert.That(viewConfig.RecentFiles, Does.Not.Contain(projectFile));
                Assert.That(viewConfig.RecentFiles, Does.Not.Contain(sceneFile));
                // Missing, but not something this deletion removed.
                Assert.That(viewConfig.RecentFiles, Does.Contain(unrelated));
            });
        }
        finally
        {
            viewConfig.RecentFiles.Remove(unrelated);
        }
    }

    [AvaloniaTest]
    public async Task Deletes_only_the_project_file_from_a_folder_it_shares()
    {
        await TestReset.ResetShellAsync();
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        // An agent can save a project straight into its output folder, next to unrelated files.
        string shared = NewWorkspace("agent-output");
        string projectFile = CreateFile(Path.Combine(shared, "clip.bep"), "{}");
        string render = CreateFile(Path.Combine(shared, "render.mp4"));
        string scene = CreateFile(Path.Combine(shared, "clip", "clip.scene"), "{}");
        viewConfig.UpdateRecentFile(projectFile);
        viewConfig.UpdateRecentProject(projectFile);
        viewConfig.UpdateRecentFile(scene);
        var shown = new List<FAContentDialog>();
        try
        {
            await CreateDeletion(shown).DeleteAsync(projectFile);

            Assert.Multiple(() =>
            {
                Assert.That(shown, Has.Count.EqualTo(1));
                Assert.That(DialogText(shown[0]), Does.Contain(MessageStrings.ConfirmDeleteProjectFileFromDisk));
                Assert.That(DialogText(shown[0]), Does.Contain(projectFile));
                Assert.That(File.Exists(projectFile), Is.False);
                Assert.That(File.Exists(render), Is.True);
                Assert.That(File.Exists(scene), Is.True);
                Assert.That(viewConfig.RecentFiles, Does.Not.Contain(projectFile));
                Assert.That(viewConfig.RecentProjects, Does.Not.Contain(projectFile));
                Assert.That(viewConfig.RecentFiles, Does.Contain(scene));
            });
        }
        finally
        {
            viewConfig.RecentFiles.Remove(scene);
        }
    }

    [AvaloniaTest]
    public async Task Cancelling_the_confirmation_deletes_nothing()
    {
        await TestReset.ResetShellAsync();
        (string projectFile, _) = await CreateClosedProjectAsync("kept", NewWorkspace("cancelled"));
        var shown = new List<FAContentDialog>();

        await CreateDeletion(shown, FAContentDialogResult.None).DeleteAsync(projectFile);

        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(1));
            Assert.That(File.Exists(projectFile), Is.True);
            Assert.That(GlobalConfiguration.Instance.ViewConfig.RecentProjects, Does.Contain(projectFile));
        });
    }

    [AvaloniaTest]
    public async Task Refuses_to_delete_an_open_project()
    {
        await TestReset.ResetShellAsync();
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new CaptureNotificationHandler();
        NotificationService.Handler = notifications;
        try
        {
            Project project = (await TestShell.Project.CreateProject(
                320, 180, 30, 44100, "busy", NewWorkspace("open")))!;
            HeadlessTestHelpers.Settle();
            string projectFile = project.Uri!.LocalPath;
            var shown = new List<FAContentDialog>();

            await CreateDeletion(shown).DeleteAsync(projectFile);

            Assert.Multiple(() =>
            {
                Assert.That(shown, Is.Empty);
                Assert.That(File.Exists(projectFile), Is.True);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(
                    notifications.All.Select(notification => notification.Message),
                    Does.Contain(MessageStrings.ProjectInUseCannotDeleteFromDisk));
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Rechecks_after_the_confirmation_that_the_project_is_still_closed()
    {
        await TestReset.ResetShellAsync();
        (string projectFile, _) = await CreateClosedProjectAsync("reopened", NewWorkspace("reopened"));
        var deletion = new ProjectDiskDeletion(TestShell.Project, TestShell.Editor)
        {
            // The project is opened again while the confirmation is showing.
            ConfirmAsync = async _ =>
            {
                await TestShell.Project.OpenProject(projectFile);
                return FAContentDialogResult.Primary;
            },
        };
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new CaptureNotificationHandler();
        NotificationService.Handler = notifications;
        try
        {
            await deletion.DeleteAsync(projectFile);

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value?.Uri?.LocalPath, Is.EqualTo(projectFile));
                Assert.That(File.Exists(projectFile), Is.True);
                Assert.That(
                    notifications.All.Select(notification => notification.Message),
                    Does.Contain(MessageStrings.ProjectInUseCannotDeleteFromDisk));
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Reports_a_project_file_that_is_already_gone()
    {
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new CaptureNotificationHandler();
        NotificationService.Handler = notifications;
        try
        {
            var shown = new List<FAContentDialog>();
            string missing = Path.Combine(NewWorkspace("missing"), "missing", "missing.bep");

            await CreateDeletion(shown).DeleteAsync(missing);

            Assert.Multiple(() =>
            {
                Assert.That(shown, Is.Empty);
                Assert.That(
                    notifications.All.Select(notification => notification.Message),
                    Does.Contain(MessageStrings.FileDoesNotExist));
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler;
        }
    }

    [Test]
    public void Keeps_a_renamed_project_folder()
    {
        string workspace = NewWorkspace("renamed");
        string projectFile = CreateFile(Path.Combine(workspace, "Wedding", "Project1.bep"), "{}");

        ProjectDiskDeletionTarget? target = ProjectDiskDeletion.Resolve(projectFile, []);

        Assert.That(target, Is.EqualTo(new ProjectDiskDeletionTarget(projectFile, projectFile, IsFolder: false)));
    }

    [Test]
    public void Keeps_a_folder_that_holds_another_project()
    {
        string workspace = NewWorkspace("nested");
        string projectFile = CreateFile(Path.Combine(workspace, "outer", "outer.bep"), "{}");
        CreateFile(Path.Combine(workspace, "outer", "copies", "copy", "COPY.BEP"), "{}");

        ProjectDiskDeletionTarget? target = ProjectDiskDeletion.Resolve(projectFile, []);

        Assert.That(target?.IsFolder, Is.False);
    }

    [Test]
    public void Keeps_a_folder_that_holds_or_is_a_protected_folder()
    {
        string workspace = NewWorkspace("protected");
        string folder = Path.Combine(workspace, "home");
        string projectFile = CreateFile(Path.Combine(folder, "home.bep"), "{}");

        Assert.Multiple(() =>
        {
            Assert.That(ProjectDiskDeletion.Resolve(projectFile, [folder])?.IsFolder, Is.False);
            Assert.That(
                ProjectDiskDeletion.Resolve(projectFile, [Path.Combine(folder, "Documents")])?.IsFolder,
                Is.False);
            // A project folder inside a protected folder is still the project's own.
            Assert.That(ProjectDiskDeletion.Resolve(projectFile, [workspace])?.IsFolder, Is.True);
        });
    }

    [Test]
    public void Protects_the_folders_the_user_and_Beutl_keep()
    {
        IReadOnlyList<string> folders = ProjectDiskDeletion.GetProtectedFolders();

        Assert.Multiple(() =>
        {
            Assert.That(folders, Does.Contain(Path.GetFullPath(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
            Assert.That(folders, Does.Contain(Path.GetFullPath(Path.GetTempPath())));
            Assert.That(folders, Does.Contain(Path.GetFullPath(BeutlEnvironment.GetHomeDirectoryPath())));
        });
    }

    [Test]
    public void Deletion_does_not_follow_links_out_of_the_project_folder()
    {
        string workspace = NewWorkspace("links");
        string projectFile = CreateFile(Path.Combine(workspace, "linked", "linked.bep"), "{}");
        string folder = Path.GetDirectoryName(projectFile)!;
        string outside = Path.Combine(workspace, "outside");
        string otherProject = CreateFile(Path.Combine(outside, "other", "other.bep"), "{}");
        File.SetAttributes(otherProject, FileAttributes.ReadOnly);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "media"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }

        try
        {
            ProjectDiskDeletionTarget? target = ProjectDiskDeletion.Resolve(projectFile, []);
            Assert.That(target, Is.EqualTo(new ProjectDiskDeletionTarget(projectFile, folder, IsFolder: true)));

            ProjectDiskDeletion.Delete(target!);

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(folder), Is.False);
                Assert.That(File.Exists(otherProject), Is.True);
                Assert.That(File.GetAttributes(otherProject).HasFlag(FileAttributes.ReadOnly), Is.True);
            });
        }
        finally
        {
            File.SetAttributes(otherProject, FileAttributes.Normal);
        }
    }

    [Test]
    public void Keeps_a_project_whose_folder_is_a_link()
    {
        string workspace = NewWorkspace("linked-folder");
        string real = Path.Combine(workspace, "real");
        CreateFile(Path.Combine(real, "alias.bep"), "{}");
        string alias = Path.Combine(workspace, "alias");
        try
        {
            Directory.CreateSymbolicLink(alias, real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }

        string projectFile = Path.Combine(alias, "alias.bep");

        Assert.That(ProjectDiskDeletion.Resolve(projectFile, [])?.IsFolder, Is.False);
    }

    [Test]
    public void Deletes_read_only_folders_and_files()
    {
        string workspace = NewWorkspace("read-only");
        string projectFile = CreateFile(Path.Combine(workspace, "locked", "locked.bep"), "{}");
        string folder = Path.GetDirectoryName(projectFile)!;
        string lockedFolder = Path.Combine(folder, "pack");
        CreateFile(Path.Combine(lockedFolder, "pack-1.idx"), "index");
        File.SetAttributes(projectFile, FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(lockedFolder, "pack-1.idx"), FileAttributes.ReadOnly);
        File.SetAttributes(lockedFolder, FileAttributes.Directory | FileAttributes.ReadOnly);
        try
        {
            ProjectDiskDeletion.Delete(new ProjectDiskDeletionTarget(projectFile, folder, IsFolder: true));

            Assert.That(Directory.Exists(folder), Is.False);
        }
        finally
        {
            // Leave nothing the isolated home's cleanup cannot remove.
            if (Directory.Exists(lockedFolder))
            {
                File.SetAttributes(lockedFolder, FileAttributes.Directory);
            }
        }
    }

    [AvaloniaTest]
    public async Task Deletion_waits_for_a_running_transition_without_cancelling_a_pending_open()
    {
        await TestReset.ResetShellAsync();
        (string nextProject, _) = await CreateClosedProjectAsync("next", NewWorkspace("pending-open"));
        await TestShell.Project.CreateProject(320, 180, 30, 44100, "closing", NewWorkspace("closing"));
        HeadlessTestHelpers.Settle();
        var closingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClosing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseChange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> blockingClosing = async (_, _) =>
        {
            closingStarted.TrySetResult();
            await releaseClosing.Task;
        };
        TestShell.Project.Closing += blockingClosing;
        try
        {
            Task closing = TestShell.Project.CloseProjectAsync();
            await closingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Requested while the close is still running, before the change below.
            Task opening = TestShell.Project.OpenProject(nextProject);
            Task change = TestShell.Project.RunExclusiveOfTransitionsAsync(async () =>
            {
                changeStarted.TrySetResult();
                await releaseChange.Task;
            });
            await Task.Delay(100);
            HeadlessTestHelpers.Settle();
            Assert.That(changeStarted.Task.IsCompleted, Is.False, "The change must wait for the running close.");

            releaseClosing.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            await changeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(opening.IsCompleted, Is.False, "The open must wait for the change.");
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            });

            releaseChange.TrySetResult();
            await change.WaitAsync(TimeSpan.FromSeconds(5));
            await opening.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(TestShell.Project.CurrentProject.Value?.Uri?.LocalPath, Is.EqualTo(nextProject));
        }
        finally
        {
            // Both hold the transition gate; a failed assertion must not leave the reset waiting.
            releaseClosing.TrySetResult();
            releaseChange.TrySetResult();
            TestShell.Project.Closing -= blockingClosing;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public void Delete_from_disk_is_offered_for_projects_only()
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string[] savedRecentFiles = viewConfig.RecentFiles.ToArray();
        string workspace = NewWorkspace("menu");
        string projectFile = Path.Combine(workspace, "menu", "menu.bep");
        string sceneFile = Path.Combine(workspace, "menu", "menu", "menu.scene");
        var view = new EditorHostFallback();
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        try
        {
            viewConfig.RecentFiles.Clear();
            viewConfig.UpdateRecentFile(sceneFile);
            viewConfig.UpdateRecentFile(projectFile);
            window.Show();
            view.GetVisualDescendants().OfType<ComboBox>().First(x => x.Name == "FilterComboBox")
                .SelectedIndex = 0;
            HeadlessTestHelpers.Render();

            ListBox recentList = view.GetVisualDescendants().OfType<ListBox>().First(x => x.Name == "recentList");
            var offered = new Dictionary<string, bool>();
            foreach (ListBoxItem item in recentList.GetRealizedContainers().OfType<ListBoxItem>())
            {
                ContextMenu menu = item.ContextMenu!;
                menu.Open(item);
                HeadlessTestHelpers.Render();
                try
                {
                    MenuItem delete = menu.Items.OfType<MenuItem>()
                        .Single(menuItem => Equals(menuItem.Header, Strings.DeleteFromDisk));
                    Separator separator = menu.Items.OfType<Separator>().Single();
                    Assert.That(separator.IsVisible, Is.EqualTo(delete.IsVisible));
                    offered[((FileInfo)item.DataContext!).FullName] = delete.IsVisible;
                }
                finally
                {
                    menu.Close();
                    HeadlessTestHelpers.Render();
                }
            }

            Assert.That(offered, Is.EquivalentTo(new Dictionary<string, bool>
            {
                [projectFile] = true,
                [sceneFile] = false,
            }));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            // Not Replace: the change-set pipeline behind the recent list reads only the first new
            // item of a Replace, and throws when there is none, as when nothing was listed before.
            viewConfig.RecentFiles.Clear();
            viewConfig.RecentFiles.AddRange(savedRecentFiles);
        }
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public System.Collections.Concurrent.ConcurrentQueue<Notification> All { get; } = new();

        public void Show(Notification notification) => All.Enqueue(notification);
    }
}
