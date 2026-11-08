using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl;

// The palettes kept with a project, in a JSON file beside the project file (Intro/Intro.bep keeps
// Intro/color-palettes.json) rather than in it, since the project file holds only what the project renders. The name
// is fixed because every project gets a folder of its own. A project without palettes has no file, and a file this
// version cannot read, such as one from a later version or one left with conflict markers, is never written over.
internal static class ProjectColorPaletteFile
{
    internal const string FileName = "color-palettes.json";

    // The format this version reads and writes. A change that this version could not read raises it.
    internal const int CurrentVersion = 1;

    private const string VersionProperty = "Version";
    private const string PalettesProperty = "Palettes";

    internal static string GetPath(string projectPath)
        => Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty, FileName);

    // The palettes in the file, or none when there is no file. A file this version cannot read gives none and
    // canWrite false.
    internal static ImmutableArray<ColorPalette> Read(string path, out bool canWrite)
    {
        canWrite = false;
        JsonNode? node;
        try
        {
            if (!File.Exists(path))
            {
                canWrite = true;
                return [];
            }

            node = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }

        if (node is not JsonObject obj
            || obj[VersionProperty] is not JsonValue version
            || version.GetValueKind() != JsonValueKind.Number
            || !version.TryGetValue(out long number)
            || number != CurrentVersion)
        {
            return [];
        }

        canWrite = true;
        return ColorPalette.ReadList(obj[PalettesProperty]);
    }

    // Writes the palettes, or removes the file when there are none.
    internal static void Write(string path, ImmutableArray<ColorPalette> palettes)
    {
        if (palettes.IsDefaultOrEmpty)
        {
            File.Delete(path);
            return;
        }

        new JsonObject
        {
            [VersionProperty] = CurrentVersion,
            [PalettesProperty] = ColorPalette.ToJson(palettes)
        }.JsonSave(path);
    }
}
