using Avalonia;
using Avalonia.Headless.NUnit;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class MainViewModelExtensionTests
{
    private static MainViewModel SharedMainViewModel => ((TestApp)Application.Current!).GetMainViewModel();

    [AvaloniaTest]
    public void ToolTabExtensions_include_the_built_in_timeline_tab()
    {
        MainViewModel vm = SharedMainViewModel;

        Assert.That(vm.ToolTabExtensions, Is.Not.Empty);
        Assert.That(vm.ToolTabExtensions, Does.Contain(TimelineTabExtension.Instance));
    }

    [AvaloniaTest]
    public void EditorExtensions_include_the_scene_editor()
    {
        MainViewModel vm = SharedMainViewModel;

        Assert.That(vm.EditorExtensions, Is.Not.Empty);
        Assert.That(vm.EditorExtensions, Does.Contain(SceneEditorExtension.Instance));
    }

    [AvaloniaTest]
    public void A_fresh_MainViewModel_sees_the_same_loaded_extensions()
    {
        var vm = new MainViewModel();

        Assert.That(vm.ToolTabExtensions, Does.Contain(TimelineTabExtension.Instance));
        Assert.That(vm.EditorExtensions, Does.Contain(SceneEditorExtension.Instance));
        Assert.That(vm.ToolWindowExtensions, Is.Not.Empty);

        vm.Dispose();
    }
}
