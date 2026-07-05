using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Workspace;

public interface IWorkspaceGuard
{
    string Root { get; }

    string ResolveForWrite(string requestedPath);
}

public sealed class WorkspaceGuard : IWorkspaceGuard
{
    private readonly StringComparison _comparison;
    private readonly string _canonicalRoot;
    private readonly string _canonicalRootWithSeparator;

    public WorkspaceGuard(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Workspace root is required.", nameof(root));
        }

        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        _comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _canonicalRoot = PathBoundary.ResolveExistingPath(Root);
        _canonicalRootWithSeparator = EnsureTrailingSeparator(_canonicalRoot);
    }

    public string Root { get; }

    public string ResolveForWrite(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new WorkspaceBoundaryException(requestedPath, null, "Write path is required.");
        }

        string absolute = Path.IsPathRooted(requestedPath)
            ? Path.GetFullPath(requestedPath)
            : Path.GetFullPath(Path.Combine(Root, requestedPath));

        string resolved = PathBoundary.ResolveDeepestExistingTarget(absolute);
        if (!IsInsideRoot(resolved))
        {
            throw new WorkspaceBoundaryException(requestedPath, resolved, "Write target is outside the configured workspace.");
        }

        return resolved;
    }

    private bool IsInsideRoot(string path)
    {
        string normalized = Path.TrimEndingDirectorySeparator(path);
        return string.Equals(normalized, _canonicalRoot, _comparison)
               || EnsureTrailingSeparator(normalized).StartsWith(_canonicalRootWithSeparator, _comparison);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}

public sealed class WorkspaceBoundaryException : Exception
{
    public WorkspaceBoundaryException(string? requestedPath, string? resolvedPath, string message)
        : base(message)
    {
        RequestedPath = requestedPath;
        ResolvedPath = resolvedPath;
    }

    public string Code => ErrorCode.WorkspaceBoundary;

    public string? RequestedPath { get; }

    public string? ResolvedPath { get; }
}
