using Avalonia.Headless.NUnit;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ShellSmokeTests
{
    [AvaloniaTest]
    public async Task CreateProject_persists_files_and_opens()
    {
        await TestReset.ResetShellAsync();

        Project? project = await TestShell.Project.CreateProject(
            1920, 1080, new Rational(30), 44100, "create", TestShell.NewWorkspace("create"));
        HeadlessTestHelpers.Settle();

        Assert.That(project, Is.Not.Null);
        Assert.That(TestShell.Project.IsOpened.Value, Is.True);
        Assert.That(BeutlApplication.Current.Project.Value, Is.SameAs(project));
        Assert.That(File.Exists(project!.Uri!.LocalPath), Is.True);

        Scene? scene = project.Items.OfType<Scene>().FirstOrDefault();
        Assert.That(scene, Is.Not.Null);
        Assert.That(File.Exists(scene!.Uri!.LocalPath), Is.True);
    }

    [AvaloniaTest]
    public async Task MainViewModel_reflects_open_and_close()
    {
        await TestReset.ResetShellAsync();

        MainViewModel vm = TestShell.MainViewModel;
        Assert.That(vm.IsProjectOpened.Value, Is.False);

        await TestShell.Project.CreateProject(640, 480, new Rational(30), 44100, "vm", TestShell.NewWorkspace("vm"));
        HeadlessTestHelpers.Settle();

        Assert.That(vm.IsProjectOpened.Value, Is.True);
        Assert.That(vm.WindowTitle.Value, Does.Contain("vm"));

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();
        Assert.That(vm.IsProjectOpened.Value, Is.False);
    }
}
