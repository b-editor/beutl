using Avalonia.Headless.NUnit;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// The scene a project opens on: the one it showed when it last closed.
[TestFixture, NonParallelizable]
public class ProjectViewStateTests
{
    // A project of two scenes in a new workspace, showing the first. Without a view state a project opens on its
    // last scene, which here is the second.
    private static async Task<(string ProjectFile, string First, string Second)> CreateTwoSceneProjectAsync(string name)
    {
        await TestReset.ResetShellAsync();
        name = $"{name}-{Guid.NewGuid():N}";
        Project project = (await TestShell.Project.CreateProject(
            320, 180, new Rational(30), 44100, name, TestShell.NewWorkspace(name)))!;
        Scene first = project.Items.OfType<Scene>().Single();
        var second = new Scene(320, 180, "second")
        {
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(first.Uri!.LocalPath)!, "second.scene")),
        };
        CoreSerializer.StoreToUri(second, second.Uri);
        project.Items.Add(second);
        CoreSerializer.StoreToUri(project, project.Uri!);
        HeadlessTestHelpers.Settle();
        TestShell.Editor.ActivateTabItem(first);
        HeadlessTestHelpers.Settle();
        return (project.Uri!.LocalPath, first.Uri.LocalPath, second.Uri.LocalPath);
    }

    private static string? SelectedScene()
        => (TestShell.Editor.SelectedTabItem.Value?.Context.Value?.Object as Scene)?.Uri?.LocalPath;

    private static async Task ReopenAsync(string projectFile)
    {
        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();
        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();
    }

    [AvaloniaTest]
    public async Task A_project_opens_on_the_scene_it_showed_when_it_closed()
    {
        (string projectFile, string first, string second) = await CreateTwoSceneProjectAsync("view-state-scene");
        try
        {
            await ReopenAsync(projectFile);
            Assert.Multiple(() =>
            {
                Assert.That(SelectedScene(), Is.EqualTo(first));
                Assert.That(TestShell.Editor.TabItems, Has.Count.EqualTo(2), "Every scene still opens.");
                Assert.That(
                    ProjectViewState.GetPath(projectFile),
                    Is.EqualTo(Path.Combine(
                        Path.GetDirectoryName(projectFile)!, ".beutl", "view-state", Path.GetFileName(projectFile) + ".config")));
            });

            TestShell.Editor.ActivateTabItem(
                TestShell.Project.CurrentProject.Value!.Items.Single(item => item.Uri!.LocalPath == second));
            HeadlessTestHelpers.Settle();
            await ReopenAsync(projectFile);

            Assert.That(SelectedScene(), Is.EqualTo(second));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_moved_project_still_opens_on_its_scene()
    {
        (string projectFile, string first, _) = await CreateTwoSceneProjectAsync("view-state-moved");
        try
        {
            await TestShell.Project.CloseProjectAsync();
            HeadlessTestHelpers.Settle();
            string folder = Path.GetDirectoryName(projectFile)!;
            string moved = folder + "-moved";
            Directory.Move(folder, moved);

            await TestShell.Project.OpenProject(Path.Combine(moved, Path.GetFileName(projectFile)));
            HeadlessTestHelpers.Settle();

            Assert.That(SelectedScene(), Is.EqualTo(Path.Combine(moved, Path.GetRelativePath(folder, first))));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    // null deletes the view state, which is how every project opened before Beutl kept one.
    [AvaloniaTest]
    [TestCase(null)]
    [TestCase("{ not json")]
    [TestCase("[]")]
    [TestCase("""{ "selected-scene": 1 }""")]
    [TestCase("""{ "selected-scene": "removed/removed.scene" }""")]
    public async Task A_view_state_without_a_scene_of_the_project_opens_it_on_its_last_scene(string? state)
    {
        (string projectFile, _, string second) = await CreateTwoSceneProjectAsync("view-state-unusable");
        try
        {
            await TestShell.Project.CloseProjectAsync();
            HeadlessTestHelpers.Settle();
            string file = ProjectViewState.GetPath(projectFile);
            Assert.That(File.Exists(file), Is.True, "The close keeps the scene on screen.");
            if (state is null)
                File.Delete(file);
            else
                File.WriteAllText(file, state);

            await TestShell.Project.OpenProject(projectFile);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentProject.Value?.Uri?.LocalPath, Is.EqualTo(projectFile));
                Assert.That(SelectedScene(), Is.EqualTo(second));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public void A_project_file_deleted_on_its_own_takes_its_view_state_along()
    {
        string file = Path.Combine(TestShell.NewWorkspace("view-state-delete"), "shared.bep");
        File.WriteAllText(file, "{}");
        string state = ProjectViewState.GetPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(state)!);
        File.WriteAllText(state, """{ "selected-scene": "shared/shared.scene" }""");

        ProjectDiskDeletion.Delete(new ProjectDiskDeletionTarget(file, file, IsFolder: false));

        Assert.That(File.Exists(state), Is.False);
    }
}
