using Avalonia.Headless.NUnit;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

public class OpenProjectTests
{
    private static void ResetProject() => TestReset.ResetShell();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    [AvaloniaTest]
    public async Task OpenProject_loads_a_persisted_project_file()
    {
        ResetProject();

        Project created = (await ProjectService.Current.CreateProject(
            1280, 720, 30, 44100, "reopen", NewWorkspace("reopen")))!;
        HeadlessTestHelpers.Settle();

        string projectFile = created.Uri!.LocalPath;
        Guid originalSceneId = created.Items.OfType<Scene>().First().Id;
        Assert.That(File.Exists(projectFile), Is.True);

        ProjectService.Current.CloseProject();
        HeadlessTestHelpers.Settle();
        Assert.That(ProjectService.Current.IsOpened.Value, Is.False);
        Assert.That(BeutlApplication.Current.Project, Is.Null);

        await ProjectService.Current.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Assert.That(ProjectService.Current.IsOpened.Value, Is.True);
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
        ResetProject();

        Project created = (await ProjectService.Current.CreateProject(
            800, 600, 25, 48000, "framesize", NewWorkspace("framesize")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = created.Uri!.LocalPath;

        ProjectService.Current.CloseProject();
        await ProjectService.Current.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Scene scene = BeutlApplication.Current.Project!.Items.OfType<Scene>().Single();
        Assert.That(scene.FrameSize.Width, Is.EqualTo(800));
        Assert.That(scene.FrameSize.Height, Is.EqualTo(600));
        Assert.That(File.Exists(scene.Uri!.LocalPath), Is.True);
    }

    [AvaloniaTest]
    public async Task OpenProject_preserves_project_variables()
    {
        ResetProject();

        Project created = (await ProjectService.Current.CreateProject(
            640, 480, 60, 22050, "vars", NewWorkspace("vars")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = created.Uri!.LocalPath;

        ProjectService.Current.CloseProject();
        await ProjectService.Current.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Project reopened = BeutlApplication.Current.Project!;
        Assert.That(reopened.Variables[ProjectVariableKeys.FrameRate], Is.EqualTo("60"));
        Assert.That(reopened.Variables[ProjectVariableKeys.SampleRate], Is.EqualTo("22050"));
    }
}
