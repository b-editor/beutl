using System.Globalization;
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
        string[] video = Extensions(decoder => decoder.VideoExtensions());
        string[] audio = Extensions(decoder => decoder.AudioExtensions());
        string[] ordered = [.. files.OrderBy(Path.GetFileName, byName)];

        // The order the element adder checks them in, since containers such as .webm and images can be on several lists.
        string[] images = [.. ordered.Where(IsImage)];
        // Where a file goes only matters among several, and it costs an open that the element adder then repeats.
        string[] videos = [.. ordered.Except(images).Where(file => Matches(file, video)
            && (ordered.Length == 1 || !Matches(file, audio) || HoldsVideo(file)))];
        string[] pictures = [.. ordered.Where(file => images.Contains(file) || videos.Contains(file))];
        string[] sounds = [.. ordered.Except(pictures).Where(file => Matches(file, audio))];

        // A video's own sound takes the layer below it.
        int soundLayer = layer + (videos.Length > 0 ? 2 : pictures.Length > 0 ? 1 : 0);
        return
        [
            .. pictures.Select(file => Describe(file, layer)),
            .. sounds.Select(file => Describe(file, soundLayer)),
        ];

        ElementDescription Describe(string file, int target)
            => new(start, length, target, new ElementSource.File(file)) { FollowsPrevious = true };
    }

    private static string[] Extensions(Func<IDecoderInfo, IEnumerable<string>> select)
        => [.. DecoderRegistry.EnumerateDecoder().SelectMany(select)];

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

    private static bool IsImage(string file) => Matches(file, SharedFilePickerOptions.ImagePatterns);

    private static bool Matches(string file, IEnumerable<string> patterns)
    {
        string extension = Path.GetExtension(file);
        return patterns.Any(pattern => string.Equals(Path.GetExtension(pattern), extension, StringComparison.OrdinalIgnoreCase));
    }
}
