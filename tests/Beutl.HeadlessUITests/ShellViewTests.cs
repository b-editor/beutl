using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Dock.Avalonia.Controls;

namespace Beutl.HeadlessUITests;

// Hosts EditView bound to a real EditViewModel in a headless window.
[TestFixture]
public class ShellViewTests
{
    [AvaloniaTest]
    [TestCase(640, false)]
    [TestCase(1280, false)]
    [TestCase(640, true)]
    [TestCase(1280, true)]
    public async Task EditView_preserves_horizontal_dock_insets_when_resized(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"dock-insets-{width}-{light}");

        var view = new EditView { DataContext = editor };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 600,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };

        try
        {
            window.Show();
            DockControl dock = view.FindControl<DockControl>("DockControl")!;
            Assert.That(dock, Is.Not.Null);

            AssertInsets(width);
            int resizedWidth = width == 640 ? 1280 : 640;
            window.Width = resizedWidth;
            AssertInsets(resizedWidth);

            void AssertInsets(int expectedWidth)
            {
                HeadlessTestHelpers.Render();
                Point origin = dock.TranslatePoint(default, window)!.Value;

                // Child docks have separate minimum-width constraints; the root gutter must
                // remain fixed even when those constraints take effect in a narrow window.
                Assert.Multiple(() =>
                {
                    Assert.That(window.ClientSize.Width, Is.EqualTo(expectedWidth));
                    Assert.That(origin.X, Is.EqualTo(2), "left dock inset");
                    Assert.That(window.ClientSize.Width - origin.X - dock.Bounds.Width,
                        Is.EqualTo(2), "right dock inset");
                });
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
