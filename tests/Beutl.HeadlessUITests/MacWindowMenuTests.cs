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
    public async Task Native_menu_populates_the_extension_entries_under_View_and_Tools()
    {
        await TestReset.ResetShellAsync();
        var window = new MacWindow { DataContext = TestShell.MainViewModel };

        try
        {
            // Showing the window would drive MainView into the real App; the menu wiring is what
            // this covers, so it is invoked directly.
            window.InitExtMenuItems(TestShell.MainViewModel);
            HeadlessTestHelpers.Settle();

            NativeMenu? rootMenu = NativeMenu.GetMenu(window);
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
            });
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }
}
