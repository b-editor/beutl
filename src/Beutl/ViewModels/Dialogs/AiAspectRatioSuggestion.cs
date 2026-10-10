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
        => Beutl.NodeGraph.Generative.GenerativeShapeSuggestion.NearestAspectRatio(ratios, frameSize, fallback);

    // The option whose ratio is nearest the frame's; 16:9 when the frame has no size.
    public static T Choose<T>(
        IReadOnlyList<T> options,
        Func<T, string> valueOf,
        PixelSize? frameSize)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0)
        {
            throw new ArgumentException(
                "At least one aspect ratio option is required.",
                nameof(options));
        }

        string ratio = Nearest(
            options.Select(valueOf).ToArray(),
            frameSize,
            "16:9");
        return options.FirstOrDefault(option => valueOf(option) == ratio) ?? options[0];
    }
}
