using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Beutl.Testing.Headless;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockGutterBackgroundTests
{
    [AvaloniaTest]
    public void Gutters_follow_the_workbench_surface_when_the_theme_changes()
    {
        // Leave the editor unbound to avoid capturing the GPU preview in the full shell.
        var view = new EditView();
        var window = new Window
        {
            Content = view,
            Width = 128,
            Height = 80,
            Background = Brushes.Magenta,
            RequestedThemeVariant = ThemeVariant.Dark
        };

        try
        {
            window.Show();
            AssertGutterPixels(ThemeVariant.Dark);
            window.RequestedThemeVariant = ThemeVariant.Light;
            AssertGutterPixels(ThemeVariant.Light);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            AssertGutterPixels(ThemeVariant.Dark);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        void AssertGutterPixels(ThemeVariant expectedTheme)
        {
            HeadlessTestHelpers.Render();
            Assert.That(view.ActualThemeVariant, Is.EqualTo(expectedTheme));
            var brush = (ISolidColorBrush)view.FindResource(expectedTheme, "DockSurfaceWorkbenchBrush")!;
            Assert.That(brush.Color.A, Is.EqualTo(255));
            if (expectedTheme == ThemeVariant.Dark)
            {
                var splitter = (ISolidColorBrush)view.FindResource(expectedTheme, "DockSplitterIdleBrush")!;
                Assert.That(brush.Color, Is.EqualTo(splitter.Color), "Dark Border gutters must match the idle splitter.");
            }

            using WriteableBitmap frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The editor window did not render.");
            using ILockedFramebuffer buffer = frame.Lock();
            Assert.That(buffer.Format, Is.EqualTo(PixelFormat.Bgra8888).Or.EqualTo(PixelFormat.Rgba8888));
            Assert.That(buffer.Size.Height, Is.GreaterThan(0));
            int gutterPixels = (int)(2 * window.RenderScaling);
            Assert.That(gutterPixels, Is.InRange(1, buffer.Size.Width / 2));

            Assert.Multiple(() =>
            {
                for (int x = 0; x < gutterPixels; x++)
                {
                    Assert.That(ReadPixel(x), Is.EqualTo(brush.Color), "left gutter pixel");
                    Assert.That(ReadPixel(buffer.Size.Width - 1 - x), Is.EqualTo(brush.Color), "right gutter pixel");
                }
            });

            Color ReadPixel(int x)
            {
                var channels = new byte[4];
                int offset = buffer.RowBytes * (buffer.Size.Height / 2) + x * 4;
                Marshal.Copy(buffer.Address + offset, channels, 0, channels.Length);
                return buffer.Format == PixelFormat.Bgra8888
                    ? Color.FromArgb(channels[3], channels[2], channels[1], channels[0])
                    : Color.FromArgb(channels[3], channels[0], channels[1], channels[2]);
            }
        }
    }
}
