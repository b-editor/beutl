using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class EditorCloseSaveTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Closing_flushes_a_pending_nudge(bool closeProject)
    {
        EditViewModel editor = await OpenEditorAsync();
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.FromSeconds(1), Length: TimeSpan.FromSeconds(3), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        HeadlessTestHelpers.Settle();
        await editor.SaveAsync();
        Element element = editor.Scene.Children.Single();
        Uri uri = element.Uri!;
        var nudge = (IElementNudgeService)editor.GetService(typeof(IElementNudgeService))!;

        nudge.Nudge(editor.Scene, [element], 1);
        TimeSpan expected = element.Start;
        if (closeProject)
            await TestShell.Project.CloseProjectAsync();
        else
            await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        HeadlessTestHelpers.Settle();

        Assert.That(CoreSerializer.RestoreFromUri<Element>(uri).Start, Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public async Task Closing_waits_for_the_file_writer_and_persists_queued_edits()
    {
        EditViewModel editor = await OpenEditorAsync();
        Uri uri = editor.Scene.Uri!;
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        IDisposable writer = await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None);
        Task close;
        try
        {
            editor.Scene.Duration = TimeSpan.FromSeconds(73);
            editor.HistoryManager.Commit();
            HeadlessTestHelpers.Settle();
            close = TestShell.Editor.CloseTabItem(tab).AsTask();
            Assert.That(close.IsCompleted, Is.False);
        }
        finally
        {
            writer.Dispose();
        }

        await close.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Settle();
        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_failed_close_save_keeps_the_editor_available_for_retry(bool closeProject)
    {
        EditViewModel editor = await OpenEditorAsync();
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        Uri uri = editor.Scene.Uri!;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);

        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == uri.LocalPath)
                       throw new IOException("Injected close save failure.");
               }))
        {
            if (closeProject)
            {
                Assert.That(await TestShell.Project.TryCloseProjectAsync(
                    TestShell.Project.CurrentProject.Value!, ProjectService.ProjectCloseIntent.SaveChanges), Is.False);
            }
            else
            {
                await TestShell.Editor.CloseTabItem(tab);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Editor.TabItems, Does.Contain(tab));
            Assert.That(tab.Context.Value, Is.SameAs(editor));
            Assert.That(editor.IsDisposingOrDisposed, Is.False);
            Assert.That(editor.IsEnabled.Value, Is.True);
            Assert.That(editor.Scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
        });
        await TestShell.Editor.CloseTabItem(tab);
        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
    }

    [AvaloniaTest]
    public async Task An_explicit_discard_does_not_persist_pending_edits()
    {
        EditViewModel editor = await OpenEditorAsync();
        Uri uri = editor.Scene.Uri!;
        TimeSpan original = editor.Scene.Duration;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);

        Assert.That(await TestShell.Project.TryCloseProjectAsync(
            TestShell.Project.CurrentProject.Value!, ProjectService.ProjectCloseIntent.DiscardChanges), Is.True);

        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(original));
    }

    [AvaloniaTest]
    public async Task Overlapping_tab_closes_do_not_dispose_the_context_twice()
    {
        EditViewModel editor = await OpenEditorAsync();
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        Uri uri = editor.Scene.Uri!;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);
        IDisposable writer = await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None);
        Task first;
        Task second;
        try
        {
            first = TestShell.Editor.CloseTabItem(tab).AsTask();
            second = TestShell.Editor.CloseTabItem(tab).AsTask();
        }
        finally { writer.Dispose(); }

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(TestShell.Editor.TabItems, Does.Not.Contain(tab));
        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Window_close_is_refused_when_a_standalone_scene_cannot_save(bool asynchronous)
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "standalone-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "main.scene")) };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        scene.Duration = TimeSpan.FromSeconds(73);

        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == scene.Uri.LocalPath)
                       throw new IOException("Injected standalone close failure.");
               }))
        {
            bool closed = asynchronous
                ? await TestShell.MainViewModel.TryDisposeForWindowCloseAsync()
                : TestShell.MainViewModel.TryDisposeForWindowClose();
            Assert.That(closed, Is.False);
        }

        Assert.That(TestShell.Editor.TabItems, Does.Contain(tab));
        Assert.That(((EditViewModel)tab.Context.Value).IsDisposingOrDisposed, Is.False);
        await TestShell.Editor.CloseTabItem(tab);
    }

    [AvaloniaTest]
    public async Task Worktree_transition_teardown_does_not_write_the_old_scene()
    {
        EditViewModel editor = await OpenEditorAsync();
        Uri uri = editor.Scene.Uri!;
        TimeSpan original = editor.Scene.Duration;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);

        using (IDisposable? mutation = TestShell.Editor.TryBeginWorktreeMutation())
        {
            Assert.That(mutation, Is.Not.Null);
            await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        }

        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(original));
    }

    private static async Task<EditViewModel> OpenEditorAsync()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool enable = config.EnableForNewProjects;
        try
        {
            config.EnableForNewProjects = false;
            string name = "close-save-" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
            Directory.CreateDirectory(directory);
            Project project = (await TestShell.Project.CreateProject(320, 180, 30, 44100, name, directory))!;
            TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().Single());
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            await editor.SaveAsync();
            return editor;
        }
        finally
        {
            config.EnableForNewProjects = enable;
        }
    }
}
