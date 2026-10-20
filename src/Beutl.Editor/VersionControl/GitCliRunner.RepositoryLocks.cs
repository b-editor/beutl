namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliRunner
{
    public RepositoryLockInfo? GetRecoverableRepositoryLock(RepositoryInfo repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (HasActiveProcess)
        {
            return null;
        }

        try
        {
            foreach (string lockPath in GetRepositoryLockPaths(repository))
            {
                if (!File.Exists(lockPath))
                {
                    continue;
                }

                var lastWriteTimeUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(lockPath), TimeSpan.Zero);
                if (_timeProvider.GetUtcNow() - lastWriteTimeUtc > StaleLockAge)
                {
                    return new RepositoryLockInfo(lockPath, lastWriteTimeUtc);
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return null;
        }
    }

    private IReadOnlyList<string> GetRepositoryLockPaths(RepositoryInfo repository)
    {
        string gitDirectory = GetGitDirectory(repository);
        List<string> lockPaths =
        [
            Path.Combine(gitDirectory, "index.lock"),
            Path.Combine(gitDirectory, "HEAD.lock"),
        ];
        try
        {
            string commonDirectory = GetCommonDirectory(gitDirectory);
            lockPaths.Add(Path.Combine(commonDirectory, "config.lock"));
            string? branchLockPath = GetCurrentBranchLockPath(gitDirectory);
            if (branchLockPath is not null)
            {
                lockPaths.Add(branchLockPath);
            }
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
        }

        return lockPaths;
    }

    private string? GetCurrentBranchLockPath(string gitDirectory)
    {
        const string refPrefix = "ref: refs/heads/";
        string head = _readAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        if (!head.StartsWith(refPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string branchPath = head[refPrefix.Length..];
        if (string.IsNullOrWhiteSpace(branchPath)
            || Path.IsPathFullyQualified(branchPath)
            || branchPath
                .Split(['/', '\\'])
                .Any(static segment => segment is "" or "." or ".."))
        {
            return null;
        }

        string commonDirectory = GetCommonDirectory(gitDirectory);
        string headsDirectory = Path.GetFullPath(
            Path.Combine(commonDirectory, "refs", "heads"));
        string refPath = Path.GetFullPath(Path.Combine(
            headsDirectory,
            branchPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!FilePathComparison.IsSameOrDescendant(headsDirectory, refPath))
        {
            return null;
        }

        if (ContainsReparsePoint(
                commonDirectory,
                Path.GetDirectoryName(refPath)!))
        {
            return null;
        }

        return refPath + ".lock";
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        string current = Path.GetFullPath(path);
        string boundary = Path.GetFullPath(root);
        while (true)
        {
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            if (FilePathComparison.AreSameCanonicalPath(current, boundary))
            {
                return false;
            }

            string? parent = Path.GetDirectoryName(current);
            if (parent is null || FilePathComparison.AreSameCanonicalPath(parent, current))
            {
                return true;
            }

            current = parent;
        }
    }

    private string GetCommonDirectory(string gitDirectory)
    {
        string commonDirectoryPath = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(commonDirectoryPath))
        {
            return gitDirectory;
        }

        string commonDirectory = _readAllText(commonDirectoryPath).Trim();
        if (!Path.IsPathFullyQualified(commonDirectory))
        {
            commonDirectory = Path.Combine(gitDirectory, commonDirectory);
        }

        return Path.GetFullPath(commonDirectory);
    }

    private string GetGitDirectory(RepositoryInfo repository)
    {
        string dotGitPath = Path.Combine(repository.RepoRoot, ".git");
        if (Directory.Exists(dotGitPath))
        {
            return Path.GetFullPath(dotGitPath);
        }

        if (File.Exists(dotGitPath))
        {
            const string prefix = "gitdir:";
            string contents = _readAllText(dotGitPath).Trim();
            if (contents.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string gitDirectory = contents[prefix.Length..].Trim();
                if (!Path.IsPathFullyQualified(gitDirectory))
                {
                    gitDirectory = Path.Combine(repository.RepoRoot, gitDirectory);
                }

                return Path.GetFullPath(gitDirectory);
            }
        }

        return Path.GetFullPath(dotGitPath);
    }
}
