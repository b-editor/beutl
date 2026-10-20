using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.DockLayoutTab;
using Beutl.Editor.Components.TerminalTab;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class MainViewModelExtensionTests
{
    [AvaloniaTest]
    public void The_composition_root_exposes_every_loaded_extension_list()
    {
        MainViewModel vm = TestShell.MainViewModel;

        Assert.Multiple(() =>
        {
            Assert.That(vm.ToolTabExtensions, Does.Contain(TimelineTabExtension.Instance));
            Assert.That(vm.ToolTabExtensions, Does.Contain(TerminalTabExtension.Instance));
            // The tab is how the user saves and applies dock layouts, so it has to be listed in
            // the tool tab menu.
            Assert.That(vm.ToolTabExtensions, Does.Contain(DockLayoutTabExtension.Instance));
            Assert.That(vm.ToolTabExtensions, Does.Contain(WebBrowserTabExtension.Instance));
            Assert.That(vm.EditorExtensions, Does.Contain(SceneEditorExtension.Instance));
            Assert.That(vm.ToolWindowExtensions, Is.Not.Empty);
        });
    }
}
