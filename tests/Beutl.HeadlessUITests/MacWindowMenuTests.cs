using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class MacWindowMenuTests
{
    [AvaloniaTest]
    public async Task Native_menu_populates_extension_entries_after_root_menus_are_reordered()
    {
        await TestReset.ResetShellAsync();
        var window = new MacWindow { DataContext = TestShell.MainViewModel };

        try
        {
            NativeMenu rootMenu = NativeMenu.GetMenu(window)!;
            NativeMenuItem viewRoot = MacWindow.FindMenuItem(rootMenu, Strings.View)!;
            NativeMenuItem toolsRoot = MacWindow.FindMenuItem(rootMenu, Strings.Tools)!;
            rootMenu.Items.Remove(viewRoot);
            rootMenu.Items.Remove(toolsRoot);
            rootMenu.Items.Insert(0, toolsRoot);
            rootMenu.Items.Insert(0, viewRoot);

            // Showing the window would drive MainView into the real App; the menu wiring is what
            // this covers, so it is invoked directly.
            window.InitExtMenuItems(TestShell.MainViewModel);
            HeadlessTestHelpers.Settle();

            NativeMenuItem? viewMenuItem = MacWindow.FindMenuItem(rootMenu, Strings.View);
            NativeMenu? editorTabMenu = MacWindow.FindMenuItem(viewMenuItem?.Menu, Strings.Editors)?.Menu;
            NativeMenu? toolTabMenu = MacWindow.FindMenuItem(viewMenuItem?.Menu, Strings.Tools)?.Menu;
            NativeMenu? toolWindowMenu = MacWindow.FindMenuItem(rootMenu, Strings.Tools)?.Menu;

            Assert.Multiple(() =>
            {
                // Resolving the wrong root item leaves every one of these empty, and the wiring
                // swallows the failure, so the menus silently lose their extension entries.
                Assert.That(editorTabMenu, Is.Not.Null);
                Assert.That(toolTabMenu, Is.Not.Null);
                Assert.That(toolWindowMenu, Is.Not.Null);
                Assert.That(editorTabMenu!.Items, Is.Not.Empty);
                Assert.That(toolTabMenu!.Items, Is.Not.Empty);
                Assert.That(toolWindowMenu!.Items, Is.Not.Empty);
            });
        }
        finally
        {
            // The per-assembly test shell owns this MainViewModel. A window used only to inspect
            // native menu wiring must not run the application shutdown path when it closes.
            window.DataContext = null;
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }
}
