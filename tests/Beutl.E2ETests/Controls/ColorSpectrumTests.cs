using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class ColorSpectrumTests
{
    [AvaloniaTest]
    public void Triangle_creates_its_bitmap_on_first_render()
    {
        var spectrum = new ColorSpectrum { Shape = FluentAvalonia.UI.Controls.ColorSpectrumShape.Triangle };
        var window = new Window { Width = 260, Height = 260, Content = spectrum };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            object? bitmap = typeof(ColorSpectrum)
                .GetField("_tempBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(spectrum);
            Assert.That(bitmap, Is.Not.Null);
        }
        finally
        {
            window.Close();
        }
    }
}
