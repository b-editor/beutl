using System.Text;

namespace Beutl.Editor.VersionControl;

// How version control treats Beutl's own paths, built from EditorConstants: the per-user state folder
// and the scratch files stay out of Git, and the project documents are stored as text with LF line
// endings. The ignore and attribute lines, the snapshot pathspecs and the path checks all come from
// here, so they name the same paths.
internal static class ProjectPathRules
{
    // The extension of the scratch files that Beutl's atomic writes leave next to a document.
    private const string TemporaryFileExtension = ".tmp";

    public static IReadOnlyList<string> DocumentExtensions { get; } =
    [
        $".{EditorConstants.ProjectFileExtension}",
        $".{EditorConstants.SceneFileExtension}",
        $".{EditorConstants.ElementFileExtension}",
    ];

    // The .gitignore lines. Git matches the folder name as written and the extension in any case.
    public static IReadOnlyList<string> GitIgnoreLines { get; } =
    [
        $"**/{EditorConstants.BeutlFolder}/",
        $"*{CreateCaseInsensitiveGlob(TemporaryFileExtension)}",
    ];

    // The .gitattributes lines for the project documents, which match the extension in any case.
    public static IReadOnlyList<string> DocumentAttributeLines { get; } =
        DocumentExtensions
            .Select(static extension => $"*{CreateCaseInsensitiveGlob(extension)} text eol=lf")
            .ToArray();

    // The glob pathspecs, relative to the project, that broad staging leaves out, in any case.
    public static IReadOnlyList<string> ExcludedPathspecSuffixes { get; } =
    [
        $"**/{CreateCaseInsensitiveGlob(EditorConstants.BeutlFolder)}/**",
        $"**/*{CreateCaseInsensitiveGlob(TemporaryFileExtension)}",
    ];

    public static bool IsTemporaryFile(string path)
        => path.EndsWith(TemporaryFileExtension, StringComparison.OrdinalIgnoreCase);

    public static bool IsStateFolderName(string name)
        => string.Equals(name, EditorConstants.BeutlFolder, StringComparison.OrdinalIgnoreCase);

    // Whether a slash-separated path relative to the project is in the state folder or a scratch file.
    public static bool IsReserved(string projectRelativePath)
        => IsTemporaryFile(projectRelativePath)
           || projectRelativePath.Split('/').Any(IsStateFolderName);

    public static string CreateCaseInsensitiveGlob(string value)
    {
        var builder = new StringBuilder(value.Length * 4);
        foreach (char character in value)
        {
            if (character is >= 'a' and <= 'z')
            {
                builder.Append('[')
                    .Append(character)
                    .Append(char.ToUpperInvariant(character))
                    .Append(']');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
