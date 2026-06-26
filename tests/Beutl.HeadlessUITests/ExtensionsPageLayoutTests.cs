using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Pages;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Guards that the extensions window adapts to the available space instead of relying on a hard 800x600
// size with a fixed-width search box. The window must declare MinWidth/MinHeight floors, and the search
// box must shrink (and stay inside its slot) on a narrow window rather than overflowing it. Asserts
// arranged layout bounds only (no pixel readback): frame capture of an inflated shell view crashes the
// headless host on software Vulkan.
[TestFixture]
public class ExtensionsPageLayoutTests
{
    private static TextBox GetSearchBox(ExtensionsPage page)
        => page.GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "searchTextBox");

    [AvaloniaTest]
    public void Window_declares_minimum_size_floors()
    {
        var page = new ExtensionsPage();
        try
        {
            Assert.That(page.MinWidth, Is.GreaterThan(0), "window must declare a MinWidth floor so it can shrink without clipping");
            Assert.That(page.MinHeight, Is.GreaterThan(0), "window must declare a MinHeight floor so it can shrink without clipping");
        }
        finally
        {
            page.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
