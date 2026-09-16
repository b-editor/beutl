using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

/// <summary>
/// File Browser writes go through the workspace admission of the host that owns the tab's editor
/// context, whichever extension authored that context, so none of them can land while Git replaces
/// the worktree.
/// </summary>
[TestFixture]
public sealed class FileBrowserWriteAdmissionTests
{
    private const int PluginPackageId = 232_600;

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
        using PluginEditorTab tab = await PluginEditorTab.OpenAsync(ProjectRoot(editor));
        using var browser = new FileBrowserTabViewModel(tab.Context);

        // The context the plugin authored serves no admission of its own; the host that opened it does.
        Assert.That(tab.Context.GetService(typeof(IProjectFileWriteAdmission)), Is.Null);
        await AssertWritesFollowWorkspaceReservation(browser, ProjectRoot(editor), coversResources: false);
    }

    [AvaloniaTest]
    public async Task A_context_no_host_owns_is_refused()
    {
        EditViewModel editor = await CreateEditor("filebrowser-admission-unowned");
        using var context = new PluginEditorContext(new PluginDocument());
        using var browser = new FileBrowserTabViewModel(context);
        Fixture fixture = Fixture.Create(ProjectRoot(editor));
        using var notifications = new NotificationCapture();

        browser.CopyFilesToDirectory([(fixture.CopySource, false)], fixture.TargetDir);
        browser.MoveFilesToDirectory([(fixture.MoveSource, false)], fixture.TargetDir);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(fixture.TargetDir, "copy.txt")), Is.False);
            Assert.That(File.Exists(fixture.MoveSource), Is.True);
            Assert.That(
                notifications.Handler.Notifications.Select(n => n.Type),
                Is.EqualTo(new[] { NotificationType.Error, NotificationType.Error }),
                "an unowned context is a wiring fault, not permission");
        });
    }

    [AvaloniaTest]
    public async Task A_second_host_does_not_admit_the_first_hosts_tabs()
    {
        EditViewModel editor = await CreateEditor("filebrowser-admission-second-host");
        using var browser = new FileBrowserTabViewModel(editor);
        Fixture fixture = Fixture.Create(ProjectRoot(editor));
        using var notifications = new NotificationCapture();
        var secondHost = new EditorService(TestShell.Extensions);

        // The second host is free; the first, which owns the editor, is mid-mutation.
        using (IDisposable? mutation = TestShell.Editor.TryBeginWorktreeMutation())
        {
            Assert.That(mutation, Is.Not.Null);
            Assert.That(secondHost.TryBeginWorktreeMutation(), Is.Not.Null);
            browser.CopyFilesToDirectory([(fixture.CopySource, false)], fixture.TargetDir);
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(fixture.TargetDir, "copy.txt")), Is.False);
            Assert.That(
                notifications.Handler.Notifications.Select(n => (n.Type, n.Message)),
                Is.EqualTo(new[] { (NotificationType.Warning, Strings.FileBrowser_WorkspaceBusy) }));
        });
    }

    [AvaloniaTest]
    public async Task Single_delete_confirmed_during_a_worktree_mutation_is_refused()
    {
        EditViewModel editor = await CreateEditor("filebrowser-admission-delete");
        using var browser = new FileBrowserTabViewModel(editor);
        Fixture fixture = Fixture.Create(ProjectRoot(editor));
        using var item = new FileSystemItemViewModel(fixture.CopySource, isDirectory: false);
        using var notifications = new NotificationCapture();

        // The mutation starts while the confirmation is open, and the user confirms into it.
        using (var confirmation = new ConfirmationDuringMutation(browser))
        {
            await browser.DeleteItemAsync(item);
            Assert.That(confirmation.Confirmed, Is.True, "the dialog must have been confirmed");
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(fixture.CopySource), Is.True, "delete");
            Assert.That(
                notifications.Handler.Notifications.Select(n => (n.Type, n.Message)),
                Is.EqualTo(new[] { (NotificationType.Warning, Strings.FileBrowser_WorkspaceBusy) }));
        });

        browser.ConfirmAsync = static _ => Task.FromResult(FAContentDialogResult.Primary);
        await browser.DeleteItemAsync(item);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(fixture.CopySource), Is.False, "delete on a free workspace");
            Assert.That(notifications.Handler.Notifications, Has.Count.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task Multi_delete_confirmed_during_a_worktree_mutation_is_refused()
    {
        EditViewModel editor = await CreateEditor("filebrowser-admission-multi-delete");
        using var browser = new FileBrowserTabViewModel(editor);
        Fixture fixture = Fixture.Create(ProjectRoot(editor));
        using var first = new FileSystemItemViewModel(fixture.CopySource, isDirectory: false);
        using var second = new FileSystemItemViewModel(fixture.MoveSource, isDirectory: false);
        using var notifications = new NotificationCapture();

        using (var confirmation = new ConfirmationDuringMutation(browser))
        {
            await browser.DeleteItemsAsync([first, second]);
            Assert.That(confirmation.Confirmed, Is.True, "the dialog must have been confirmed");
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(fixture.CopySource), Is.True, "first");
            Assert.That(File.Exists(fixture.MoveSource), Is.True, "second");
            Assert.That(
                notifications.Handler.Notifications.Select(n => (n.Type, n.Message)),
                Is.EqualTo(new[] { (NotificationType.Warning, Strings.FileBrowser_WorkspaceBusy) }),
                "one refusal covers the whole batch");
        });

        browser.ConfirmAsync = static _ => Task.FromResult(FAContentDialogResult.Primary);
        await browser.DeleteItemsAsync([first, second]);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(fixture.CopySource), Is.False, "first on a free workspace");
            Assert.That(File.Exists(fixture.MoveSource), Is.False, "second on a free workspace");
            Assert.That(notifications.Handler.Notifications, Has.Count.EqualTo(1));
        });
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
            browser.CopyFilesToResources([(Path.Combine(fixture.SourceDir, "renamed.txt"), false)]);

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

    /// <summary>
    /// Stands in for the user: while the confirmation dialog is open a worktree mutation begins, and
    /// then the user confirms. The mutation ends when this is disposed.
    /// </summary>
    private sealed class ConfirmationDuringMutation : IDisposable
    {
        private IDisposable? _mutation;

        public ConfirmationDuringMutation(FileBrowserTabViewModel browser)
        {
            browser.ConfirmAsync = _ =>
            {
                _mutation = TestShell.Editor.TryBeginWorktreeMutation();
                Assert.That(_mutation, Is.Not.Null, "the workspace must be free while the dialog is open");
                Confirmed = true;
                return Task.FromResult(FAContentDialogResult.Primary);
            };
        }

        public bool Confirmed { get; private set; }

        public void Dispose() => _mutation?.Dispose();
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

    /// <summary>
    /// A document opened through a plugin's <see cref="EditorExtension"/>, so its editor context is
    /// one the host did not author but does own, exactly as a real out-of-tree editor would be.
    /// </summary>
    private sealed class PluginEditorTab : IDisposable
    {
        private readonly PluginDocument _document;

        private PluginEditorTab(PluginDocument document, IEditorContext context)
        {
            _document = document;
            Context = context;
        }

        public IEditorContext Context { get; }

        public static async Task<PluginEditorTab> OpenAsync(string projectRoot)
        {
            var document = new PluginDocument
            {
                Uri = new Uri(Path.Combine(projectRoot, "document" + PluginEditorExtension.FileExtension)),
            };
            TestShell.Extensions.AddExtensions(PluginPackageId, [PluginEditorExtension.Instance]);
            try
            {
                TestShell.Editor.ActivateTabItem(document);
                HeadlessTestHelpers.Settle();
                Assert.That(TestShell.Editor.TryGetTabItem(document, out EditorTabItem? tab), Is.True);
                Assert.That(tab!.Context.Value, Is.TypeOf<PluginEditorContext>());
                return new PluginEditorTab(document, tab.Context.Value);
            }
            catch
            {
                TestShell.Extensions.RemoveExtensions(PluginPackageId);
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                TestShell.Editor.CloseTabItem(_document).AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                TestShell.Extensions.RemoveExtensions(PluginPackageId);
            }
        }
    }

    private sealed class PluginEditorExtension : EditorExtension
    {
        public const string FileExtension = ".admissiondoc";

        public static readonly PluginEditorExtension Instance = new();

        public override string Name => "FileBrowserWriteAdmissionPluginEditor";

        public override string DisplayName => Name;

        public override FilePickerFileType GetFilePickerFileType() => new(Name)
        {
            Patterns = ["*" + FileExtension],
        };

        public override FAIconSource? GetIcon() => null;

        public override bool TryCreateEditor(CoreObject obj, [NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [NotNullWhen(true)] out IEditorContext? context)
        {
            context = new PluginEditorContext(obj);
            return true;
        }

        public override bool MatchFileExtension(string ext) => ext == FileExtension;
    }

    /// <summary>An editor context the host did not author: it serves no host services at all.</summary>
    private sealed class PluginEditorContext(CoreObject document) : IEditorContext
    {
        public CoreObject Object => document;

        public EditorExtension Extension => PluginEditorExtension.Instance;

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

    private sealed class PluginDocument : CoreObject;
}
