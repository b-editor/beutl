using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl;

// The palettes kept with a project, in a JSON file beside the project file and named after it (MyProject.bep keeps
// MyProject.palettes.json) rather than in it, since the project file holds only what the project renders. A project
// without palettes has no file, and a file this version cannot read, such as one from a later version or one left
// with conflict markers, is never written over.
internal static class ProjectColorPaletteFile
{
    internal const string Suffix = ".palettes.json";

    // The format this version reads and writes. A change that this version could not read raises it.
    internal const int CurrentVersion = 1;

    private const string VersionProperty = "Version";
    private const string PalettesProperty = "Palettes";

    internal static string GetPath(string projectPath)
        => Path.Combine(
            Path.GetDirectoryName(projectPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(projectPath) + Suffix);

    // The palettes in the file, or none when there is no file. A file this version cannot read gives none and
    // canWrite false.
    internal static ImmutableArray<ColorPalette> Read(string path, out bool canWrite)
    {
        canWrite = false;
        try
        {
            if (!File.Exists(path))
            {
                canWrite = true;
                return [];
            }

            // A duplicate key throws only when the object is first read, so the reads stay inside the guard.
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject obj
                || obj[VersionProperty] is not JsonValue version
                || version.GetValueKind() != JsonValueKind.Number
                || !version.TryGetValue(out long number)
                || number != CurrentVersion)
            {
                return [];
            }

            ImmutableArray<ColorPalette> palettes = ColorPalette.ReadList(obj[PalettesProperty]);
            canWrite = true;
            return palettes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                      or ArgumentException or InvalidOperationException)
        {
            return [];
        }
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

    // Gives a project saved under another name the palettes of the one it was saved from, as they are on disk, so the
    // copy keeps palettes of its own. A copy of a project without palettes gets none, even where a file is left.
    internal static void CopyForSaveAs(string sourceProjectPath, string targetProjectPath)
    {
        string source = GetPath(sourceProjectPath);
        string target = GetPath(targetProjectPath);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), PathBoundary.Comparison))
        {
            return;
        }

        if (File.Exists(source))
        {
            File.Copy(source, target, overwrite: true);
        }
        else
        {
            File.Delete(target);
        }
    }
}
