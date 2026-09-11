using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;

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
    [TestCase(false)]
    [TestCase(true)]
    public async Task Failed_project_switch_restores_the_previous_project_and_edits(bool failActivation)
    {
        await ResetProjectAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose;
        bool oldAutoSave = GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>? corruptTarget = null;
        Func<Project, Task>? rejectTarget = null;
        try
        {
            config.AutoCommitOnClose = false;
            Project original = (await TestShell.Project.CreateProject(320, 180, 30, 44100,
                "original", NewWorkspace($"failed-project-switch-{failActivation}")))!;
            Scene scene = original.Items.OfType<Scene>().Single();
            var otherScene = new Scene(320, 180, "other")
            {
                Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "other.scene")),
            };
            CoreSerializer.StoreToUri(otherScene, otherScene.Uri);
            original.Items.Add(otherScene);
            CoreSerializer.StoreToUri(original, original.Uri!);
            HeadlessTestHelpers.Settle();
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            Assert.That(TestShell.Editor.SelectedTabItem.Value?.Context.Value?.Object, Is.SameAs(scene));
            string target = Path.Combine(Path.GetDirectoryName(original.Uri!.LocalPath)!, "target.bep");
            File.Copy(original.Uri.LocalPath, target);
            GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled = false;
            scene.Duration = TimeSpan.FromSeconds(73);

            bool failedAtExpectedStage = false;
            if (failActivation)
            {
                rejectTarget = project =>
                {
                    if (project.Uri!.LocalPath == target)
                    {
                        failedAtExpectedStage = true;
                        throw new IOException("The target project cannot be activated.");
                    }
                    return Task.CompletedTask;
                };
                TestShell.Project.Opened += rejectTarget;
            }
            else
            {
                corruptTarget = (_, _) =>
                {
                    failedAtExpectedStage = true;
                    File.WriteAllText(target, "{ invalid json");
                    return Task.CompletedTask;
                };
                TestShell.Project.ClosingFinalizing += corruptTarget;
            }

            var published = new List<Project?>();
            using IDisposable subscription = TestShell.Project.ProjectObservable.Subscribe(change => published.Add(change.New));
            await TestShell.Project.OpenProject(target);

            Assert.Multiple(() =>
            {
                Assert.That(failedAtExpectedStage, Is.True);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(original));
                Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(2));
                Assert.That(TestShell.Editor.SelectedTabItem.Value?.Context.Value?.Object, Is.SameAs(scene));
                Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
                Assert.That(published.LastOrDefault(), Is.SameAs(original));
                Assert.That(GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile, Is.EqualTo(original.Uri.LocalPath));
            });
            Assert.That(await TestShell.Editor.SelectedTabItem.Value!.Commands.Value!.OnSave(), Is.True);
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
        }
        finally
        {
            if (corruptTarget is not null) TestShell.Project.ClosingFinalizing -= corruptTarget;
            if (rejectTarget is not null) TestShell.Project.Opened -= rejectTarget;
            try { await ResetProjectAsync(); }
            finally
            {
                config.AutoCommitOnClose = oldAutoCommitOnClose;
                GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled = oldAutoSave;
            }
        }
    }

    [AvaloniaTest]
    public async Task OpenProject_consults_preflight_before_refusing_a_missing_project_file()
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

            Assert.Multiple(() =>
            {
                // Consulted even though the file is gone: an interrupted pull leaves it missing,
                // and the preflight is what restores it before the open continues.
                Assert.That(preflightCalls, Is.EqualTo(1));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            });
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

        await TestShell.Project.CloseProjectAsync();
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

        await TestShell.Project.CloseProjectAsync();
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

        await TestShell.Project.CloseProjectAsync();
        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Project reopened = BeutlApplication.Current.Project!;
        Assert.That(reopened.Variables[ProjectVariableKeys.FrameRate], Is.EqualTo("60"));
        Assert.That(reopened.Variables[ProjectVariableKeys.SampleRate], Is.EqualTo("22050"));
    }

    [AvaloniaTest]
    public async Task OpenProject_plain_resave_preserves_an_old_project_version()
    {
        await ResetProjectAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "plain-old-version", NewWorkspace("plain-old-version")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;

        await TestShell.Project.CloseProjectAsync();
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
