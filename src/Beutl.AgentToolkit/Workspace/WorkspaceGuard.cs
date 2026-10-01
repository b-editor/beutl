using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Workspace;

public interface IWorkspaceGuard
{
    string Root { get; }

    string ResolveForWrite(string requestedPath);
}

public sealed class WorkspaceGuard : IWorkspaceGuard
{
    private readonly string _canonicalRoot;

    public WorkspaceGuard(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Workspace root is required.", nameof(root));
        }

        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        _canonicalRoot = PathBoundary.ResolveExistingPath(Root);
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
        // Both paths use their on-disk spelling. macOS and Windows can also host
        // case-sensitive directories, so an OS-wide case-insensitive comparison is unsafe.
        if (!FilePathComparison.IsSameOrDescendantCanonicalPath(
                Path.TrimEndingDirectorySeparator(_canonicalRoot), Path.TrimEndingDirectorySeparator(resolved)))
        {
            throw new WorkspaceBoundaryException(requestedPath, resolved, "Write target is outside the configured workspace.");
        }

        return resolved;
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
