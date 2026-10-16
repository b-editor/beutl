using System.Text;

namespace Beutl.Editor.VersionControl;

// Beutl's own paths for version control, from EditorConstants: the state folder and scratch files stay out
// of Git, and project documents are text with LF endings. Every rule, line and check below comes from here.
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

    // Git matches the folder name as written and the extension in any case.
    public static IReadOnlyList<string> GitIgnoreLines { get; } =
    [
        $"**/{EditorConstants.BeutlFolder}/",
        $"*{CreateCaseInsensitiveGlob(TemporaryFileExtension)}",
    ];

    // These match the extension in any case.
    public static IReadOnlyList<string> DocumentAttributeLines { get; } =
        DocumentExtensions
            .Select(static extension => $"*{CreateCaseInsensitiveGlob(extension)} text eol=lf")
            .ToArray();

    // Relative to the project; broad staging leaves these out.
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
