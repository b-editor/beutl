using Avalonia.Headless.NUnit;
using Beutl.Pages;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Guards that the extensions window declares MinWidth/MinHeight floors so it can shrink.
[TestFixture]
public class ExtensionsPageLayoutTests
{
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
