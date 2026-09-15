using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

/// <summary>
/// File Browser writes go through the host-owned workspace admission, whichever editor context the
/// tab belongs to, so none of them can land while Git replaces the worktree.
/// </summary>
[TestFixture]
public sealed class FileBrowserWriteAdmissionTests
{
    [AvaloniaTest]
    public async Task Built_in_editor_context_writes_wait_out_a_worktree_mutation()
    {
        EditViewModel editor = await CreateEditor("filebrowser-admission-builtin");
        using var browser = new FileBrowserTabViewModel(editor);

        await AssertWritesFollowWorkspaceReservation(browser, ProjectRoot(editor), coversResources: true);
    }

    [AvaloniaTest]
    public async Task Plugin_provided_editor_context_writes_wait_out_a_worktree_mutation()
    {
        EditViewModel editor = await CreateEditor("filebrowser-admission-plugin");
        using var context = new PluginEditorContext();
        using var browser = new FileBrowserTabViewModel(context);

        // A context the host did not author serves no admission service of its own.
        Assert.That(context.GetService(typeof(IProjectFileWriteAdmission)), Is.Null);
        await AssertWritesFollowWorkspaceReservation(browser, ProjectRoot(editor), coversResources: false);
    }

    [AvaloniaTest]
    public async Task Writes_are_refused_when_no_host_admission_is_installed()
    {
        EditViewModel editor = await CreateEditor("filebrowser-admission-missing");
        using var browser = new FileBrowserTabViewModel(editor);
        Fixture fixture = Fixture.Create(ProjectRoot(editor));
        IProjectFileWriteAdmission? installed = HostProjectFileWriteAdmission.Current;
        Assert.That(installed, Is.Not.Null, "the shell must install the host admission");
        using var notifications = new NotificationCapture();
        try
        {
            HostProjectFileWriteAdmission.Current = null;

            browser.CopyFilesToDirectory([(fixture.CopySource, false)], fixture.TargetDir);
            browser.MoveFilesToDirectory([(fixture.MoveSource, false)], fixture.TargetDir);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(fixture.TargetDir, "copy.txt")), Is.False);
                Assert.That(File.Exists(fixture.MoveSource), Is.True);
                Assert.That(
                    notifications.Handler.Notifications.Select(n => n.Type),
                    Is.EqualTo(new[] { NotificationType.Error, NotificationType.Error }),
                    "a missing admission is a wiring fault, not permission");
            });
        }
        finally
        {
            HostProjectFileWriteAdmission.Current = installed;
        }

        browser.CopyFilesToDirectory([(fixture.CopySource, false)], fixture.TargetDir);
        Assert.That(File.Exists(Path.Combine(fixture.TargetDir, "copy.txt")), Is.True);
    }

    private static async Task AssertWritesFollowWorkspaceReservation(
        FileBrowserTabViewModel browser,
        string projectRoot,
        bool coversResources)
    {
        Fixture fixture = Fixture.Create(projectRoot);
        // Imports land beside the project file, which is not always the folder the scene sits in.
        string resourcesDir = Path.Combine(browser.ProjectDirectory ?? projectRoot, "resources");
        browser.RootPath.Value = fixture.TargetDir;
        HeadlessTestHelpers.Settle();
        using var renameItem = new FileSystemItemViewModel(fixture.RenameSource, isDirectory: false);
        using var notifications = new NotificationCapture();

        IDisposable? mutation = TestShell.Editor.TryBeginWorktreeMutation();
        Assert.That(mutation, Is.Not.Null, "the workspace must be free before the mutation starts");
        int rejectedWrites;
        using (mutation)
        {
            browser.CopyFilesToDirectory([(fixture.CopySource, false)], fixture.TargetDir);
            browser.MoveFilesToDirectory([(fixture.MoveSource, false)], fixture.TargetDir);
            browser.CreateNewFolder();
            await browser.RenameItemAsync(renameItem, "renamed.txt");
            rejectedWrites = 4;
            if (coversResources)
            {
                browser.CopyFilesToResources([(fixture.CopySource, false)]);
                browser.MoveFilesToResources([(fixture.MoveSource, false)]);
                rejectedWrites += 2;
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(fixture.TargetDir, "copy.txt")), Is.False, "copy");
                Assert.That(File.Exists(fixture.MoveSource), Is.True, "move");
                Assert.That(File.Exists(fixture.RenameSource), Is.True, "rename");
                Assert.That(Directory.GetDirectories(fixture.TargetDir), Is.Empty, "new folder");
                if (coversResources)
                    Assert.That(Directory.Exists(resourcesDir), Is.False, "resource import");
                Assert.That(
                    notifications.Handler.Notifications.Select(n => (n.Type, n.Message)),
                    Is.EqualTo(Enumerable.Repeat(
                        (NotificationType.Warning, Strings.FileBrowser_WorkspaceBusy),
                        rejectedWrites)),
                    "every refused write must tell the user the workspace is busy");
            });
        }

        browser.CopyFilesToDirectory([(fixture.CopySource, false)], fixture.TargetDir);
        browser.MoveFilesToDirectory([(fixture.MoveSource, false)], fixture.TargetDir);
        browser.CreateNewFolder();
        await browser.RenameItemAsync(renameItem, "renamed.txt");
        if (coversResources)
            browser.CopyFilesToResources([(fixture.RenameSource.Replace("rename.txt", "renamed.txt"), false)]);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(fixture.TargetDir, "copy.txt")), Is.True, "copy");
            Assert.That(File.Exists(Path.Combine(fixture.TargetDir, "move.txt")), Is.True, "move");
            Assert.That(File.Exists(Path.Combine(fixture.SourceDir, "renamed.txt")), Is.True, "rename");
            Assert.That(Directory.GetDirectories(fixture.TargetDir), Has.Length.EqualTo(1), "new folder");
            if (coversResources)
                Assert.That(File.Exists(Path.Combine(resourcesDir, "renamed.txt")), Is.True, "resource import");
            Assert.That(
                notifications.Handler.Notifications,
                Has.Count.EqualTo(rejectedWrites),
                "writes on a free workspace must not raise notifications");
        });
    }

    private static async Task<EditViewModel> CreateEditor(string name)
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 180, 30, 44100, name, directory))!;
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static string ProjectRoot(EditViewModel editor)
        => Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;

    private sealed record Fixture(
        string SourceDir,
        string TargetDir,
        string CopySource,
        string MoveSource,
        string RenameSource)
    {
        public static Fixture Create(string projectRoot)
        {
            string sourceDir = Path.Combine(projectRoot, "admission-sources");
            string targetDir = Path.Combine(projectRoot, "admission-target");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(targetDir);
            var fixture = new Fixture(
                sourceDir,
                targetDir,
                Path.Combine(sourceDir, "copy.txt"),
                Path.Combine(sourceDir, "move.txt"),
                Path.Combine(sourceDir, "rename.txt"));
            File.WriteAllText(fixture.CopySource, "copy");
            File.WriteAllText(fixture.MoveSource, "move");
            File.WriteAllText(fixture.RenameSource, "rename");
            return fixture;
        }
    }

    private sealed class NotificationCapture : IDisposable
    {
        private readonly INotificationServiceHandler _previous = NotificationService.Handler;

        public NotificationCapture()
        {
            NotificationService.Handler = Handler;
        }

        public CaptureNotificationHandler Handler { get; } = new();

        public void Dispose()
        {
            NotificationService.Handler = _previous;
        }
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification) => Notifications.Add(notification);
    }

    /// <summary>An editor context the host did not author: it serves no host services at all.</summary>
    private sealed class PluginEditorContext : IEditorContext
    {
        public CoreObject Object { get; } = new PluginObject();

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public object? GetService(Type serviceType) => null;

        public void Dispose() => IsEnabled.Dispose();
    }

    private sealed class PluginObject : CoreObject;
}
