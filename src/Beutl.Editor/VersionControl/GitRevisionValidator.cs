namespace Beutl.Editor.VersionControl;

internal static class GitRevisionValidator
{
    public static void ValidateCommitId(string revision, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision, paramName);
        if (revision.Length is < 4 or > 64
            || revision.Any(static character => character is not (>= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "The commit revision must be a hexadecimal object ID between 4 and 64 characters.",
                paramName);
        }
    }
}
