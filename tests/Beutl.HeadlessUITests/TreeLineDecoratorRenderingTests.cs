using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Beutl.Controls;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TreeLineDecoratorRenderingTests
{
    [AvaloniaTest]
    [TestCase(1d)]
    [TestCase(1.25d)]
    [TestCase(1.5d)]
    [TestCase(1.75d)]
    [TestCase(2d)]
    public void Nesting_line_matches_separator_pixel_thickness_without_blurred_edges(double scale)
    {
        var line = new TreeLineDecorator
        {
            Width = 48,
            Height = 32,
            IndentLevel = 2,
            LineBrush = Brushes.White,
            UseLayoutRounding = true
        };
        var separator = new Separator
        {
            Width = 48,
            Height = 1,
            Margin = default,
            Background = Brushes.White,
            UseLayoutRounding = true
        };
        Canvas.SetTop(separator, 40);
        var window = new Window
        {
            Width = 64,
            Height = 64,
            Background = Brushes.Black,
            Content = new Canvas { Children = { line, separator } }
        };
        try
        {
            window.Show();
            window.SetRenderScaling(scale);
            HeadlessTestHelpers.Render(3);
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Format, Is.EqualTo(PixelFormat.Bgra8888).Or.EqualTo(PixelFormat.Rgba8888));
            int stride = frame.PixelSize.Width * 4;
            var pixels = new byte[stride * frame.PixelSize.Height];
            IntPtr buffer = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                frame.CopyPixels(new PixelRect(frame.PixelSize), buffer, pixels.Length, stride);
                Marshal.Copy(buffer, pixels, 0, pixels.Length);
            }
            finally { Marshal.FreeHGlobal(buffer); }

            // White on black makes both partially covered and solid pixels measurable,
            // independently of whether the frame uses RGBA or BGRA ordering.
            byte Red(int x, int y) => pixels[y * stride + x * 4];
            byte[] vertical = Enumerable.Range(0, (int)(16 * scale))
                .Select(x => Red(x, (int)(16 * scale))).Where(value => value > 0).ToArray();
            byte[] horizontal = Enumerable.Range(0, frame.PixelSize.Height)
                .Select(y => Red((int)(36 * scale), y)).Where(value => value > 0).ToArray();
            Assert.That(horizontal, Has.Length.EqualTo((int)Math.Ceiling(scale)));
            Assert.That(vertical, Has.Length.EqualTo(horizontal.Length));
            Assert.That(vertical, Is.All.EqualTo(byte.MaxValue), "A nesting line must not have half-covered edge pixels.");
            Assert.That(horizontal, Is.All.EqualTo(byte.MaxValue));
        }
        finally { window.Close(); }
    }
}
