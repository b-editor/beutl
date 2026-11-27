namespace Beutl.Editor.VersionControl;

internal static class GitRevisionValidator
{
    public static bool IsHexadecimalRevision(string? revision)
        => revision is { Length: >= 4 and <= 64 } && revision.All(char.IsAsciiHexDigit);

    public static bool IsAbbreviatedObjectId(string revision)
        => revision.Length is not (40 or 64) && IsHexadecimalRevision(revision);

    // Matches the Web dashboard's isGitRevision. Pass a verified object ID to checkout.
    public static bool IsWebRevision(string? revision)
        => revision is { Length: > 0 and <= 255 }
            && !revision.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\uFEFF')
            && revision.IndexOfAny(['~', '^', ':', '?', '*', '[', '\\', ']', '|']) < 0
            && !revision.StartsWith('/') && !revision.EndsWith('/')
            && !revision.EndsWith(".lock", StringComparison.Ordinal)
            && !revision.Contains("..", StringComparison.Ordinal)
            && !revision.Contains("@{", StringComparison.Ordinal);

    public static void ValidateCommitId(string revision, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision, paramName);
        if (!IsHexadecimalRevision(revision))
        {
            throw new ArgumentException(
                "The commit revision must be a hexadecimal object ID between 4 and 64 characters.",
                paramName);
        }
    }
}
