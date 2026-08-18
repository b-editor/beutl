using System.Globalization;
using Beutl.Media;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// Picks the offered aspect ratio closest to the scene an asset is being made
/// for. Both the image and the video dialog ask the same question, and the
/// image one used to answer it with fixed sizes that had no 16:9 at all — a
/// widescreen project was offered 3:2 and the result never fitted the frame.
/// </summary>
internal static class AiAspectRatioSuggestion
{
    public static string Nearest(
        IReadOnlyList<string> ratios,
        PixelSize? frameSize,
        string fallback)
    {
        ArgumentNullException.ThrowIfNull(ratios);
        if (ratios.Count == 0)
            throw new ArgumentException("At least one aspect ratio is required.", nameof(ratios));

        if (frameSize is not { Width: > 0, Height: > 0 } size)
            return ratios.Contains(fallback) ? fallback : ratios[0];

        double target = (double)size.Width / size.Height;
        string? nearest = null;
        double nearestDistance = double.PositiveInfinity;
        foreach (string ratio in ratios)
        {
            if (!TryParse(ratio, out double candidate))
                continue;
            // Compared in log space so 16:9 and 9:16 sit an equal distance from
            // square; a linear difference would always favour the wider one.
            double distance = Math.Abs(Math.Log(target) - Math.Log(candidate));
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = ratio;
            }
        }

        return nearest ?? (ratios.Contains(fallback) ? fallback : ratios[0]);
    }

    private static bool TryParse(string value, out double ratio)
    {
        ratio = 0;
        string[] parts = value.Split(':');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        ratio = width / height;
        return true;
    }
}
