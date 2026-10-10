using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using PickerColorComponent = FluentAvalonia.UI.Controls.ColorComponent;

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

    [AvaloniaTest]
    public void Triangle_skips_a_subpixel_bitmap()
    {
        var spectrum = new ColorSpectrum
        {
            Shape = FluentAvalonia.UI.Controls.ColorSpectrumShape.Triangle,
            MinWidth = 0,
            MinHeight = 0,
            UseLayoutRounding = false,
            Width = 4.5,
            Height = 4.5,
        };
        var window = new Window { Width = 100, Height = 100, UseLayoutRounding = false, Content = spectrum };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(spectrum.Bounds.Width, Is.GreaterThan(4));
            Assert.That(spectrum.Bounds.Width, Is.LessThan(5));

            object? bitmap = typeof(ColorSpectrum)
                .GetField("_tempBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(spectrum);
            Assert.That(bitmap, Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Spectrum_rebuilds_its_bitmap_for_each_component()
    {
        var spectrum = new ColorSpectrum();
        var window = new Window { Width = 260, Height = 260, Content = spectrum };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var bitmapField = typeof(ColorSpectrum)
                .GetField("_tempBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!;

            foreach (PickerColorComponent component in new[]
            {
                PickerColorComponent.Hue,
                PickerColorComponent.Saturation,
                PickerColorComponent.Value,
                PickerColorComponent.Red,
                PickerColorComponent.Green,
                PickerColorComponent.Blue,
            })
            {
                spectrum.Component = component;
                HeadlessTestHelpers.Render();
                Assert.That(bitmapField.GetValue(spectrum), Is.Not.Null, $"Missing {component} bitmap.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Wheel_draws_the_color_picked_at_each_point()
    {
        // Saturation 0 keeps the selector at the center, away from the sampled points
        var spectrum = new ColorSpectrum
        {
            Shape = FluentAvalonia.UI.Controls.ColorSpectrumShape.Wheel,
            Color = Color2.FromHSVf(0, 0, 1),
        };
        var window = new Window { Width = 260, Height = 260, Content = spectrum };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            AssertDrawsPickedColors(window, spectrum, WheelPoints(spectrum));

            spectrum.Color = Color2.FromHSVf(0, 0, 0.5f);
            HeadlessTestHelpers.Render();
            AssertDrawsPickedColors(window, spectrum, WheelPoints(spectrum));

            window.Width = 360;
            window.Height = 360;
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.That(spectrum.Bounds.Width, Is.EqualTo(360));
            AssertDrawsPickedColors(window, spectrum, WheelPoints(spectrum));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Triangle_draws_the_color_picked_at_each_point()
    {
        // Full saturation and value keep the selector at the hue corner, away from the sampled points
        var spectrum = new ColorSpectrum
        {
            Shape = FluentAvalonia.UI.Controls.ColorSpectrumShape.Triangle,
            Color = Color2.FromHSVf(40, 1, 1),
        };
        var window = new Window { Width = 260, Height = 260, Content = spectrum };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            AssertDrawsPickedColors(window, spectrum, TrianglePoints(spectrum, 40));

            spectrum.Color = Color2.FromHSVf(250, 1, 1);
            HeadlessTestHelpers.Render();
            AssertDrawsPickedColors(window, spectrum, TrianglePoints(spectrum, 250));

            // A dispatcher job lays out the resize; the next tick renders it
            window.Width = 360;
            window.Height = 360;
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.That(spectrum.Bounds.Width, Is.EqualTo(360));
            var bitmap = (WriteableBitmap)typeof(ColorSpectrum)
                .GetField("_tempBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(spectrum)!;
            Assert.That(bitmap.PixelSize, Is.EqualTo(new PixelSize(356, 356)));
            AssertDrawsPickedColors(window, spectrum, TrianglePoints(spectrum, 250));
        }
        finally
        {
            window.Close();
        }
    }

    private static Rect WheelRect(ColorSpectrum spectrum)
    {
        double size = Math.Min(spectrum.Bounds.Width, spectrum.Bounds.Height) - 4;
        return new Rect(spectrum.Bounds.Width / 2 - size / 2, spectrum.Bounds.Height / 2 - size / 2, size, size);
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y - radius * Math.Sin(radians));
    }

    private static IEnumerable<Point> WheelPoints(ColorSpectrum spectrum)
    {
        Rect wheel = WheelRect(spectrum);
        foreach (double hue in new[] { 30d, 100, 150, 210, 270, 330 })
        {
            foreach (double saturation in new[] { 0.35, 0.7, 0.9 })
                yield return PointAt(wheel.Center, wheel.Width / 2 * saturation, hue);
        }
    }

    private static IEnumerable<Point> TrianglePoints(ColorSpectrum spectrum, double hue)
    {
        Rect wheel = WheelRect(spectrum);
        double outerRadius = wheel.Width / 2 - 2;
        double ringThickness = 50 * wheel.Width / 500;
        double cornerRadius = outerRadius - ringThickness;

        // The hue ring, away from the selector line at the current hue
        foreach (double offset in new[] { 60d, 120, 180, 240, 300 })
            yield return PointAt(wheel.Center, outerRadius - ringThickness / 2, hue + offset);

        // The triangle, away from its edges and from the selector at the hue corner
        Point hueCorner = PointAt(wheel.Center, cornerRadius, hue);
        Point leftCorner = PointAt(wheel.Center, cornerRadius, hue + 120);
        Point rightCorner = PointAt(wheel.Center, cornerRadius, hue - 120);
        foreach ((double h, double l, double r) in new[]
                 {
                     (1 / 3d, 1 / 3d, 1 / 3d), (0.5, 0.25, 0.25), (0.15, 0.7, 0.15), (0.15, 0.15, 0.7),
                     (0.1, 0.45, 0.45),
                 })
        {
            yield return new Point(
                hueCorner.X * h + leftCorner.X * l + rightCorner.X * r,
                hueCorner.Y * h + leftCorner.Y * l + rightCorner.Y * r);
        }
    }

    // Compares each drawn pixel with the color a click on it picks
    private static void AssertDrawsPickedColors(Window window, ColorSpectrum spectrum, IEnumerable<Point> points)
    {
        const int Tolerance = 4;
        Color2 color = spectrum.Color;
        var mismatches = new List<string>();
        using (WriteableBitmap frame = window.CaptureRenderedFrame()!)
        {
            foreach (Point point in points)
            {
                Point inWindow = spectrum.TranslatePoint(point, window)!.Value;
                var pixel = new PixelPoint((int)Math.Floor(inWindow.X), (int)Math.Floor(inWindow.Y));
                (int R, int G, int B) drawn = ReadPixel(frame, pixel);

                var pixelCenter = new Point(pixel.X + 0.5, pixel.Y + 0.5);
                spectrum.Color = color;
                window.MouseDown(pixelCenter, MouseButton.Left);
                window.MouseUp(pixelCenter, MouseButton.Left);
                Color2 picked = spectrum.Color;

                if (Math.Abs(drawn.R - picked.R) > Tolerance
                    || Math.Abs(drawn.G - picked.G) > Tolerance
                    || Math.Abs(drawn.B - picked.B) > Tolerance)
                {
                    mismatches.Add($"{pixel}: drawn {drawn}, picked ({picked.R}, {picked.G}, {picked.B})");
                }
            }
        }

        spectrum.Color = color;
        HeadlessTestHelpers.Render();
        Assert.That(mismatches, Is.Empty);
    }

    private static unsafe (int R, int G, int B) ReadPixel(WriteableBitmap frame, PixelPoint point)
    {
        using ILockedFramebuffer framebuffer = frame.Lock();
        byte* pixel = (byte*)framebuffer.Address + point.Y * framebuffer.RowBytes + point.X * 4;
        if (framebuffer.Format == PixelFormat.Rgba8888)
            return (pixel[0], pixel[1], pixel[2]);

        Assert.That(framebuffer.Format, Is.EqualTo(PixelFormat.Bgra8888));
        return (pixel[2], pixel[1], pixel[0]);
    }
}
