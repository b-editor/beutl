using Avalonia.Headless.NUnit;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ShellSmokeTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    [AvaloniaTest]
    public async Task CreateProject_persists_files_and_opens()
    {
        await ResetProjectAsync();

        Project? project = await ProjectService.Current.CreateProject(
            1920, 1080, 30, 44100, "create", NewWorkspace("create"));
        HeadlessTestHelpers.Settle();

        Assert.That(project, Is.Not.Null);
        Assert.That(ProjectService.Current.IsOpened.Value, Is.True);
        Assert.That(BeutlApplication.Current.Project, Is.SameAs(project));
        Assert.That(File.Exists(project!.Uri!.LocalPath), Is.True);

        Scene? scene = project.Items.OfType<Scene>().FirstOrDefault();
        Assert.That(scene, Is.Not.Null);
        Assert.That(File.Exists(scene!.Uri!.LocalPath), Is.True);
    }

    [AvaloniaTest]
    public async Task ActivateTabItem_creates_a_scene_editor_tab()
    {
        await ResetProjectAsync();

        Project? project = await ProjectService.Current.CreateProject(
            640, 480, 30, 44100, "tab", NewWorkspace("tab"));
        HeadlessTestHelpers.Settle();
        Scene scene = project!.Items.OfType<Scene>().First();

        EditorService.Current.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        Assert.That(EditorService.Current.TabItems, Is.Not.Empty);
    }

    [AvaloniaTest]
    public async Task MainViewModel_reflects_open_and_close()
    {
        await ResetProjectAsync();

        var vm = new MainViewModel();
        Assert.That(vm.IsProjectOpened.Value, Is.False);

        await ProjectService.Current.CreateProject(640, 480, 30, 44100, "vm", NewWorkspace("vm"));
        HeadlessTestHelpers.Settle();

        Assert.That(vm.IsProjectOpened.Value, Is.True);
        Assert.That(vm.WindowTitle.Value, Does.Contain("vm"));

        ProjectService.Current.CloseProject();
        HeadlessTestHelpers.Settle();
        Assert.That(vm.IsProjectOpened.Value, Is.False);

        vm.Dispose();
    }
}
