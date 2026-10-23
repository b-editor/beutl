using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Extensibility;
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

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Failed_project_switch_restores_the_previous_project_and_edits(bool failActivation)
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool oldAutoCommitOnClose = config.AutoCommitOnClose.Value;
        bool oldAutoSave = EditViewModel.IsAutoSaveSuppressedForTesting;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task>? corruptTarget = null;
        Func<Project, Task>? rejectTarget = null;
        Action<Project?>? recordTransition = null;
        try
        {
            config.AutoCommitOnClose.Value = false;
            Project original = (await TestShell.Project.CreateProject(320, 180, new Rational(30), 44100,
                "original", TestShell.NewWorkspace($"failed-project-switch-{failActivation}")))!;
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
            EditViewModel.IsAutoSaveSuppressedForTesting = true;
            scene.Duration.Value = MediaTime.FromSeconds(73);

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
            recordTransition = published.Add;
            TestShell.Project.TransitionCommitted += recordTransition;
            await TestShell.Project.OpenProject(target);

            Assert.Multiple(() =>
            {
                Assert.That(failedAtExpectedStage, Is.True);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(original));
                Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(2));
                Assert.That(TestShell.Editor.SelectedTabItem.Value?.Context.Value?.Object, Is.SameAs(scene));
                Assert.That(scene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(73)));
                Assert.That(published.LastOrDefault(), Is.SameAs(original));
                Assert.That(GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile.Value, Is.EqualTo(original.Uri.LocalPath));
            });
            Assert.That(await ((ISavableEditorContext)TestShell.Editor.SelectedTabItem.Value!.Context.Value).SaveAsync(), Is.True);
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration.Value, Is.EqualTo(MediaTime.FromSeconds(73)));
        }
        finally
        {
            if (corruptTarget is not null) TestShell.Project.ClosingFinalizing -= corruptTarget;
            if (rejectTarget is not null) TestShell.Project.Opened -= rejectTarget;
            if (recordTransition is not null) TestShell.Project.TransitionCommitted -= recordTransition;
            try { await TestReset.ResetShellAsync(); }
            finally
            {
                config.AutoCommitOnClose.Value = oldAutoCommitOnClose;
                EditViewModel.IsAutoSaveSuppressedForTesting = oldAutoSave;
            }
        }
    }

    [Test]
    public async Task ProjectOpenAttempt_complete_waits_for_in_progress_cancellation()
    {
        var attempt = new ProjectService.ProjectOpenAttempt();
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
        await Assert.DoesNotThrowAsync(async () => await cancel.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [AvaloniaTest]
    public async Task OpenProject_loads_a_persisted_project_file()
    {
        await TestReset.ResetShellAsync();

        Project created = (await TestShell.Project.CreateProject(
            800, 600, new Rational(60), 22050, "reopen", TestShell.NewWorkspace("reopen")))!;
        HeadlessTestHelpers.Settle();

        string projectFile = created.Uri!.LocalPath;
        Guid originalSceneId = created.Items.OfType<Scene>().First().Id.Value;
        Assert.That(File.Exists(projectFile), Is.True);

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();
        Assert.That(TestShell.Project.IsOpened.Value, Is.False);
        Assert.That(BeutlApplication.Current.Project.Value, Is.Null);

        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Assert.That(TestShell.Project.IsOpened.Value, Is.True);
        Project reopened = BeutlApplication.Current.Project.Value!;
        Assert.That(reopened, Is.Not.Null);
        Scene reopenedScene = reopened.Items.OfType<Scene>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(reopened, Is.Not.SameAs(created));
            Assert.That(reopened.Uri!.LocalPath, Is.EqualTo(projectFile));
            Assert.That(reopenedScene.Id.Value, Is.EqualTo(originalSceneId));
            Assert.That(reopenedScene.FrameSize.Value.Width, Is.EqualTo(800));
            Assert.That(reopenedScene.FrameSize.Value.Height, Is.EqualTo(600));
            Assert.That(File.Exists(reopenedScene.Uri!.LocalPath), Is.True);
            Assert.That(reopened.FrameRate.Value, Is.EqualTo(new Rational(60)));
            Assert.That(reopened.SampleRate.Value, Is.EqualTo(22050));
        });
    }

    [AvaloniaTest]
    public async Task OpenProject_resave_records_the_version_that_saved_it()
    {
        await TestReset.ResetShellAsync();

        Project project = (await TestShell.Project.CreateProject(
            640, 480, new Rational(30), 44100, "resave-version", TestShell.NewWorkspace("resave-version")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();

        JsonObject legacyProject = JsonNode.Parse(await File.ReadAllTextAsync(projectFile))!.AsObject();
        legacyProject["appVersion"] = LegacyAppVersion;
        legacyProject.JsonSave(projectFile);

        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();
        // Until the project is saved again, its file names the version that saved it last.
        Assert.That(BeutlApplication.Current.Project.Value!.AppVersion, Is.EqualTo(LegacyAppVersion));

        await TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        JsonObject savedProject = JsonNode.Parse(await File.ReadAllTextAsync(projectFile))!.AsObject();
        Assert.That((string?)savedProject["appVersion"], Is.EqualTo(BeutlApplication.Version));
    }
}
