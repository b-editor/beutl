using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class OpenProjectTests
{
    private const string LegacyAppVersion = "2.0.0";

    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    [AvaloniaTest]
    public async Task OpenProject_skips_preflight_when_the_project_file_is_missing()
    {
        await ResetProjectAsync();
        string missingFile = Path.Combine(NewWorkspace("missing-preflight"), "missing.bep");
        int preflightCalls = 0;
        Func<ProjectService.ProjectOpenAttempt, CancellationToken,
            Task<ProjectService.ProjectOpenPreparation?>> preflight = (_, _) =>
        {
            preflightCalls++;
            return Task.FromResult<ProjectService.ProjectOpenPreparation?>(null);
        };
        TestShell.Project.OpeningPreflight += preflight;
        try
        {
            await TestShell.Project.OpenProject(missingFile);

            Assert.That(preflightCalls, Is.Zero);
        }
        finally
        {
            TestShell.Project.OpeningPreflight -= preflight;
        }
    }

    [Test]
    public async Task ProjectOpenAttempt_complete_waits_for_in_progress_cancellation()
    {
        var attempt = new ProjectService.ProjectOpenAttempt(1, "project.bep");
        var cancellationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = attempt.CancellationToken.Register(() =>
        {
            cancellationEntered.TrySetResult();
            releaseCancellation.Task.GetAwaiter().GetResult();
        });

        Task cancel = Task.Run(attempt.CancelIfPending);
        await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotThrow(attempt.Complete);
        releaseCancellation.TrySetResult();
        Assert.DoesNotThrowAsync(async () => await cancel.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [AvaloniaTest]
    public async Task OpenProject_loads_a_persisted_project_file()
    {
        await ResetProjectAsync();

        Project created = (await TestShell.Project.CreateProject(
            1280, 720, 30, 44100, "reopen", NewWorkspace("reopen")))!;
        HeadlessTestHelpers.Settle();

        string projectFile = created.Uri!.LocalPath;
        Guid originalSceneId = created.Items.OfType<Scene>().First().Id;
        Assert.That(File.Exists(projectFile), Is.True);

        await TestShell.Project.CloseProject();
        HeadlessTestHelpers.Settle();
        Assert.That(TestShell.Project.IsOpened.Value, Is.False);
        Assert.That(BeutlApplication.Current.Project, Is.Null);

        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Assert.That(TestShell.Project.IsOpened.Value, Is.True);
        Project reopened = BeutlApplication.Current.Project!;
        Assert.That(reopened, Is.Not.Null);
        Assert.That(reopened, Is.Not.SameAs(created));
        Assert.That(reopened.Uri!.LocalPath, Is.EqualTo(projectFile));

        Scene reopenedScene = reopened.Items.OfType<Scene>().Single();
        Assert.That(reopenedScene.Id, Is.EqualTo(originalSceneId));
    }

    [AvaloniaTest]
    public async Task OpenProject_round_trips_frame_size()
    {
        await ResetProjectAsync();

        Project created = (await TestShell.Project.CreateProject(
            800, 600, 25, 48000, "framesize", NewWorkspace("framesize")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = created.Uri!.LocalPath;

        await TestShell.Project.CloseProject();
        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Scene scene = BeutlApplication.Current.Project!.Items.OfType<Scene>().Single();
        Assert.That(scene.FrameSize.Width, Is.EqualTo(800));
        Assert.That(scene.FrameSize.Height, Is.EqualTo(600));
        Assert.That(File.Exists(scene.Uri!.LocalPath), Is.True);
    }

    [AvaloniaTest]
    public async Task OpenProject_preserves_project_variables()
    {
        await ResetProjectAsync();

        Project created = (await TestShell.Project.CreateProject(
            640, 480, 60, 22050, "vars", NewWorkspace("vars")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = created.Uri!.LocalPath;

        await TestShell.Project.CloseProject();
        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Project reopened = BeutlApplication.Current.Project!;
        Assert.That(reopened.Variables[ProjectVariableKeys.FrameRate], Is.EqualTo("60"));
        Assert.That(reopened.Variables[ProjectVariableKeys.SampleRate], Is.EqualTo("22050"));
    }

    [AvaloniaTest]
    public async Task OpenProject_advances_project_version_after_migrating_a_legacy_element()
    {
        await ResetProjectAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "migration", NewWorkspace("migration")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;
        Scene scene = project.Items.OfType<Scene>().Single();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(1),
            Layer: 0,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.Single();
        string elementFile = element.Uri!.LocalPath;
        Assert.That(await editor.Commands!.OnSave(), Is.True);
        HeadlessTestHelpers.Settle();

        await TestShell.Project.CloseProject();
        HeadlessTestHelpers.Settle();

        JsonObject legacyElement = JsonNode.Parse(await File.ReadAllTextAsync(elementFile))!.AsObject();
        Assert.That(legacyElement.Remove(nameof(Element.Objects)), Is.True);
        legacyElement["Operation"] = new JsonObject
        {
            ["Children"] = new JsonArray(),
        };
        legacyElement.JsonSave(elementFile);

        JsonObject legacyProject = JsonNode.Parse(await File.ReadAllTextAsync(projectFile))!.AsObject();
        legacyProject["appVersion"] = LegacyAppVersion;
        legacyProject.JsonSave(projectFile);

        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Project reopened = BeutlApplication.Current.Project!;
        Scene reopenedScene = reopened.Items.OfType<Scene>().Single();
        Assert.That(reopened.AppVersion, Is.EqualTo(BeutlApplication.Version));

        TestShell.Editor.ActivateTabItem(reopenedScene);
        HeadlessTestHelpers.Settle();
        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        JsonObject savedProject = JsonNode.Parse(await File.ReadAllTextAsync(projectFile))!.AsObject();
        JsonObject savedElement = JsonNode.Parse(await File.ReadAllTextAsync(elementFile))!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That((string?)savedProject["appVersion"], Is.EqualTo(BeutlApplication.Version));
            Assert.That(savedElement[nameof(Element.Objects)], Is.TypeOf<JsonArray>());
            Assert.That(savedElement[nameof(Element.Objects)]!.AsArray().Count, Is.Zero);
            Assert.That(savedElement["Operation"], Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task OpenProject_plain_resave_preserves_an_old_project_version()
    {
        await ResetProjectAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "plain-old-version", NewWorkspace("plain-old-version")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;

        await TestShell.Project.CloseProject();
        HeadlessTestHelpers.Settle();

        JsonObject legacyProject = JsonNode.Parse(await File.ReadAllTextAsync(projectFile))!.AsObject();
        legacyProject["appVersion"] = LegacyAppVersion;
        legacyProject.JsonSave(projectFile);

        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();
        Assert.That(BeutlApplication.Current.Project!.AppVersion, Is.EqualTo(LegacyAppVersion));

        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        JsonObject savedProject = JsonNode.Parse(await File.ReadAllTextAsync(projectFile))!.AsObject();
        Assert.That((string?)savedProject["appVersion"], Is.EqualTo(LegacyAppVersion));
    }
}
