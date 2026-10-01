using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class ProjectPackageExportTests
{
    [AvaloniaTest]
    public async Task Export_waits_for_pending_writes_and_includes_the_live_scene()
    {
        (Project project, EditViewModel editor, string output) = await OpenProjectAsync();
        IDisposable writer = await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None);
        Task<ExportResult> export;
        try
        {
            editor.Scene.Duration = TimeSpan.FromSeconds(73);
            editor.HistoryManager.Commit();
            project.Name = "Current project name";
            export = TestShell.MainViewModel.ExportProjectAsync(project, output);
            Assert.That(export.IsCompleted, Is.False);
            Assert.That(editor.IsEnabled.Value, Is.False);
        }
        finally { writer.Dispose(); }

        Assert.That((await export.WaitAsync(TimeSpan.FromSeconds(15))).Success, Is.True);
        Project imported = await ImportAsync(output);
        Assert.That(imported.Name, Is.EqualTo("Current project name"));
        Assert.That(imported.Items.OfType<Scene>().Single().Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
        Assert.That(editor.IsEnabled.Value, Is.True);
    }

    [AvaloniaTest]
    public async Task Export_flushes_pending_nudges()
    {
        (Project project, EditViewModel editor, string output) = await OpenProjectAsync();
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.FromSeconds(1), Length: TimeSpan.FromSeconds(3), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        await editor.SaveAsync();
        Element element = editor.Scene.Children.Single();
        var nudge = (IElementNudgeService)editor.GetService(typeof(IElementNudgeService))!;
        nudge.Nudge(editor.Scene, [element], 1);
        TimeSpan expected = element.Start;

        Assert.That((await TestShell.MainViewModel.ExportProjectAsync(project, output)).Success, Is.True);

        Project imported = await ImportAsync(output);
        Assert.That(imported.Items.OfType<Scene>().Single().Children.Single().Start, Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public async Task A_failed_save_preserves_the_previous_package_and_allows_retry()
    {
        (Project project, EditViewModel editor, string output) = await OpenProjectAsync();
        File.WriteAllText(output, "previous complete package");
        editor.Scene.Duration = TimeSpan.FromSeconds(73);
        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == editor.Scene.Uri!.LocalPath)
                       throw new IOException("save failed");
               }))
        {
            Assert.That(await CaptureFailure(TestShell.MainViewModel.ExportProjectAsync(project, output)), Is.TypeOf<IOException>());
        }

        Assert.That(File.ReadAllText(output), Is.EqualTo("previous complete package"));
        Assert.That(editor.IsEnabled.Value, Is.True);
        Assert.That((await TestShell.MainViewModel.ExportProjectAsync(project, output).WaitAsync(TimeSpan.FromSeconds(15))).Success, Is.True);
        Assert.That((await ImportAsync(output)).Items.OfType<Scene>().Single().Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
    }

    [AvaloniaTest]
    public async Task Export_holds_off_project_close_and_worktree_mutation()
    {
        (Project project, _, string output) = await OpenProjectAsync();
        Task? close = null;
        bool closeWaited = false;
        bool mutationRefused = false;
        var progress = new InlineProgress(value =>
        {
            if (value.Progress != 0.1) return;
            using IDisposable? mutation = TestShell.Editor.TryBeginWorktreeMutation();
            mutationRefused = mutation is null;
            close = TestShell.Project.CloseProjectAsync();
            closeWaited = !close.IsCompleted;
        });

        ExportResult result = await TestShell.MainViewModel.ExportProjectAsync(project, output, progress);
        Assert.That(close, Is.Not.Null);
        await close!.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.That(result.Success, Is.True);
        Assert.That(closeWaited, Is.True);
        Assert.That(mutationRefused, Is.True);
        Assert.That((await ImportAsync(output)).Items.OfType<Scene>().Count(), Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task A_project_replaced_while_the_picker_was_open_is_not_exported()
    {
        (Project previous, _, string output) = await OpenProjectAsync();
        await OpenProjectAsync();

        ExportResult result = await TestShell.MainViewModel.ExportProjectAsync(previous, output);

        Assert.That(result.Success, Is.False);
        Assert.That(File.Exists(output), Is.False);
    }

    [AvaloniaTest]
    public async Task Cancellation_while_waiting_for_a_writer_releases_the_export_guards()
    {
        (Project project, EditViewModel editor, string output) = await OpenProjectAsync();
        using var cancellation = new CancellationTokenSource();
        using (await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None))
        {
            Task<ExportResult> export = TestShell.MainViewModel.ExportProjectAsync(project, output, cancellationToken: cancellation.Token);
            Assert.That(export.IsCompleted, Is.False);
            cancellation.Cancel();
            Assert.That(await CaptureFailure(export), Is.InstanceOf<OperationCanceledException>());
        }

        Assert.That(editor.IsEnabled.Value, Is.True);
        Assert.That(File.Exists(output), Is.False);
        Assert.That((await TestShell.MainViewModel.ExportProjectAsync(project, output).WaitAsync(TimeSpan.FromSeconds(15))).Success, Is.True);
    }

    private static async Task<(Project Project, EditViewModel Editor, string Output)> OpenProjectAsync()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool enable = config.EnableForNewProjects;
        try
        {
            config.EnableForNewProjects = false;
            string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, "package-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Project project = (await TestShell.Project.CreateProject(64, 64, 30, 44100, "Project", root))!;
            TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().Single());
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            await editor.SaveAsync();
            return (project, editor, Path.Combine(root, "export.beutlpkg"));
        }
        finally { config.EnableForNewProjects = enable; }
    }

    private static async Task<Project> ImportAsync(string archive)
    {
        string destination = Path.Combine(BeutlHomeIsolation.CurrentHome!, "imported-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        return (await ProjectPackageService.Current.ImportAsync(archive, destination))!;
    }

    private static async Task<Exception?> CaptureFailure(Task task)
    {
        try { await task; return null; }
        catch (Exception ex) { return ex; }
    }

    private sealed class InlineProgress(Action<(string Message, double Progress)> report) : IProgress<(string Message, double Progress)>
    {
        public void Report((string Message, double Progress) value) => report(value);
    }
}
