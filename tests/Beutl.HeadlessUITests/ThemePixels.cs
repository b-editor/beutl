using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Testing.Headless;
using FluentAvalonia.Styling;

namespace Beutl.HeadlessUITests;

// Checks that a custom-drawn control paints with its window's theme rather than with fixed colors.
internal static class ThemePixels
{
    public static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark, FluentAvaloniaTheme.HighContrastTheme];

    // Asserts that the rendered pixel at `point` in `control` is the theme brush `key` painted over the window's background.
    public static void AssertBrushOverWindow(Control control, Point point, string key)
    {
        var window = (Window)TopLevel.GetTopLevel(control)!;
        ThemeVariant theme = window.ActualThemeVariant;
        Assert.That(control.TryFindResource(key, theme, out object? resource), Is.True, $"{key} in {theme}");
        var fill = (ISolidColorBrush)resource!;
        Color under = ((ISolidColorBrush)window.Background!).Color;
        double alpha = fill.Color.A / 255.0 * fill.Opacity;
        int[] expected = [Mix(fill.Color.R, under.R), Mix(fill.Color.G, under.G), Mix(fill.Color.B, under.B)];

        Point at = control.TranslatePoint(point, window)!.Value;
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        (int r, int g, int b) = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint((int)at.X, (int)at.Y));
        Assert.That(new[] { r, g, b }, Is.EqualTo(expected).Within(2), $"{key} in {theme} at {point}");

        int Mix(byte color, byte background) => (int)Math.Round(background + (color - background) * alpha);
    }
}
