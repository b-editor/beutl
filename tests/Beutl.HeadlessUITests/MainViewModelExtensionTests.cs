using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.TerminalTab;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class MainViewModelExtensionTests
{
    private static MainViewModel SharedMainViewModel => TestShell.MainViewModel;

    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    [AvaloniaTest]
    public async Task ToolTabExtensions_include_the_built_in_timeline_tab()
    {
        await ResetProjectAsync();
        MainViewModel vm = SharedMainViewModel;

        Assert.That(vm.ToolTabExtensions, Is.Not.Empty);
        Assert.That(vm.ToolTabExtensions, Does.Contain(TimelineTabExtension.Instance));
    }

    [AvaloniaTest]
    public async Task ToolTabExtensions_include_the_built_in_terminal_tab()
    {
        await ResetProjectAsync();
        MainViewModel vm = SharedMainViewModel;

        Assert.That(vm.ToolTabExtensions, Does.Contain(TerminalTabExtension.Instance));
    }

    [AvaloniaTest]
    public async Task EditorExtensions_include_the_scene_editor()
    {
        await ResetProjectAsync();
        MainViewModel vm = SharedMainViewModel;

        Assert.That(vm.EditorExtensions, Is.Not.Empty);
        Assert.That(vm.EditorExtensions, Does.Contain(SceneEditorExtension.Instance));
    }

    [AvaloniaTest]
    public async Task The_composition_root_exposes_every_loaded_extension_list()
    {
        await ResetProjectAsync();
        MainViewModel vm = SharedMainViewModel;

        Assert.That(vm.ToolTabExtensions, Does.Contain(TimelineTabExtension.Instance));
        Assert.That(vm.EditorExtensions, Does.Contain(SceneEditorExtension.Instance));
        Assert.That(vm.ToolWindowExtensions, Is.Not.Empty);
    }
}
