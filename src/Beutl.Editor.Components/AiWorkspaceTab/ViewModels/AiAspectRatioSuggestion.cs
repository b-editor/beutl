using Beutl.Media;
using Beutl.NodeGraph.Generative;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

/// <summary>Picks the offered aspect ratio closest to the scene's frame; shared by the image and video dialogs.</summary>
internal static class AiAspectRatioSuggestion
{
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

        string ratio = GenerativeShapeSuggestion.NearestAspectRatio(
            options.Select(valueOf).ToArray(),
            frameSize,
            "16:9");
        return options.First(option => valueOf(option) == ratio);
    }
}
