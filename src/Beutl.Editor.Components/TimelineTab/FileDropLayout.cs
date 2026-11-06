using System.Globalization;
using Beutl.Editor.Components.Helpers;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.Editor.Components.TimelineTab;

// Lays out files dropped together: in name order with numbers in order (1, 2, 10), since a drag's own order starts at
// whichever file the pointer held, and one after another from the drop. Sound goes on a layer of its own, because
// background music is meant to run under the pictures. Files no decoder claims are left out.
internal static class FileDropLayout
{
    public static ElementDescription[] Create(IEnumerable<string> files, MediaTime start, MediaTime length, int layer)
    {
        var byName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.NumericOrdering);
        string[] video = [.. MediaFileMatcher.VideoExtensions()];
        string[] audio = [.. MediaFileMatcher.AudioExtensions()];
        string[] ordered = [.. files.OrderBy(Path.GetFileName, byName)];

        var pictures = new List<string>();
        var sounds = new List<string>();
        bool anyVideo = false;
        foreach (string file in ordered)
        {
            // The order the element adder checks them in, since containers such as .webm and images can be on several lists.
            if (MediaFileMatcher.IsImage(file))
            {
                pictures.Add(file);
            }
            // Where a file goes only matters among several, and it costs an open that the element adder then repeats.
            else if (MediaFileMatcher.Matches(file, video)
                     && (ordered.Length == 1 || !MediaFileMatcher.Matches(file, audio) || HoldsVideo(file)))
            {
                pictures.Add(file);
                anyVideo = true;
            }
            else if (MediaFileMatcher.Matches(file, audio))
            {
                sounds.Add(file);
            }
        }

        // A video's own sound takes the layer below it.
        int soundLayer = layer + (anyVideo ? 2 : pictures.Count > 0 ? 1 : 0);
        return
        [
            .. pictures.Select(file => Describe(file, layer)),
            .. sounds.Select(file => Describe(file, soundLayer)),
        ];

        ElementDescription Describe(string file, int target)
            => new(start, length, target, new ElementSource.File(file)) { FollowsPrevious = true };
    }

    // Sound alone can come in a container that is on both lists, such as .webm, which only opening it tells.
    private static bool HoldsVideo(string file)
    {
        try
        {
            using var reader = MediaReader.Open(file, new MediaOptions(MediaMode.AudioVideo));
            return reader.HasVideo;
        }
        catch (Exception)
        {
            // The element adder says what it makes of a file that does not open.
            return true;
        }
    }
}
