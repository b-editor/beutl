using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.Common;

// File paths given to the tools must be absolute. Neither host has a base directory an agent can
// rely on (the in-app host runs inside the editor's process), so a relative path is refused rather
// than resolved against a directory the agent did not choose.
public static class ToolPaths
{
    public static string RequireAbsolute(string? path, string target)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"'{target}' is required.",
                target));
        }

        // Windows treats "\dir" and "C:dir" as rooted, yet both still depend on the current drive
        // or directory, so only a fully qualified path is accepted.
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"'{path}' is not an absolute path.",
                target,
                "Pass an absolute path, for example the project's folder joined with the file name."));
        }

        return Path.GetFullPath(path);
    }

    // The file a write lands on, after following links and taking the on-disk spelling, so that
    // overwrite checks and same-file comparisons look at the file that is actually written.
    public static string ResolveForWrite(string? path, string target)
        => FilePathComparison.ResolveCanonicalPath(RequireAbsolute(path, target));
}
