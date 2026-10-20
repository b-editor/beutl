using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    // Git tracks a symbolic link as the link entry itself, never as the tree or blob it points to, so
    // the tracked path of a project file is its own entry name inside its canonical parent: a link in
    // the final component keeps the name Git stores, while an alias directory above it resolves to
    // the real tree. Callers verify canonical containment before asking.
    internal static string GetRepositoryRelativeProjectFilePath(
        RepositoryInfo repository,
        string projectFile)
    {
        string canonicalProjectRoot = FilePathComparison.ResolveCanonicalPath(repository.ProjectRoot);
        string fullPath = Path.GetFullPath(projectFile);
        string canonicalParent = FilePathComparison.ResolveCanonicalPath(
            Path.GetDirectoryName(fullPath) ?? fullPath);
        // Containment is decided on canonical paths compared ordinally, like every other repository
        // check; a relative path could compare the two parents by platform case rules instead. An
        // entry whose parent sits outside the project root can only be tracked as its resolved target.
        string trackedProjectFile = FilePathComparison.IsSameOrDescendant(canonicalProjectRoot, canonicalParent)
            ? ResolveTrackedProjectEntry(canonicalParent, Path.GetFileName(fullPath))
            : FilePathComparison.ResolveCanonicalPath(projectFile);
        string projectRelativePath = NormalizeGitPath(
            Path.GetRelativePath(canonicalProjectRoot, trackedProjectFile));
        return repository.Pathspec == "."
            ? projectRelativePath
            : repository.Pathspec + "/" + projectRelativePath;
    }

    // The canonical parent plus the entry's own on-disk spelling. The entry itself is deliberately
    // not resolved, because following it would replace a tracked link with its target's name.
    private static string ResolveTrackedProjectEntry(string canonicalParent, string name)
    {
        string candidate = Path.Combine(canonicalParent, name);
        if (!Path.Exists(candidate))
        {
            // Only an entry that exists under this spelling is renormalized; on a case-sensitive
            // volume a sole case-insensitive neighbour is a different file, not this one.
            return candidate;
        }

        try
        {
            return FilePathComparison.SelectCanonicalExistingEntry(
                name,
                candidate,
                Directory.EnumerateFileSystemEntries(canonicalParent));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or NotSupportedException)
        {
            // A parent that cannot be listed keeps the caller's spelling rather than failing the lookup.
            return candidate;
        }
    }

    // An unignored nested repository would become a gitlink; refuse it before anything is staged.
    private static async Task EnsureNoNestedRepositoryWouldBeStagedAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        await ThrowIfNestedRepositoryWouldBeStagedAsync(
                repository,
                runner,
                GetProjectPathPrefix(repository),
                FindNestedRepositories(repository.ProjectRoot),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ThrowIfNestedRepositoryWouldBeStagedAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string prefix,
        IReadOnlyList<string> nestedRepositories,
        CancellationToken cancellationToken)
    {
        if (nestedRepositories.Count == 0)
        {
            return;
        }

        HashSet<string> ignored;
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["check-ignore", "--stdin", "-z"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    StandardInput: string.Concat(nestedRepositories.Select(path => $"{prefix}{path}/\0")),
                    UseLiteralPathspecs: false),
                cancellationToken).ConfigureAwait(false);
            ignored = GitCliRunner.SplitNullSeparated(result.Stdout)
                .Select(static path => path.TrimEnd('/'))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            ignored = [];
        }

        // An unignored repository would become a gitlink in the snapshot, so refuse it before anything is
        // staged rather than after.
        string? staged = nestedRepositories.FirstOrDefault(path => !ignored.Contains(prefix + path));
        if (staged is not null)
        {
            throw new InvalidOperationException(
                $"The nested Git repository '{staged}' cannot be snapshotted safely.");
        }
    }

    // Project-relative paths of the Git repositories inside the project. The walk never enters a
    // repository it found, Beutl's or Git's own state folders, or a linked folder, and it skips a
    // folder it cannot list, as Git does.
    private static IReadOnlyList<string> FindNestedRepositories(string projectRoot)
    {
        var result = new List<string>();
        if (!Directory.Exists(projectRoot))
        {
            return result;
        }

        var pending = new Stack<string>();
        pending.Push(projectRoot);
        var options = new EnumerationOptions { AttributesToSkip = 0 };
        while (pending.TryPop(out string? directory))
        {
            string[] children;
            try
            {
                children = Directory.GetDirectories(directory, "*", options);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       && !string.Equals(directory, projectRoot, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(child));
                if (ProjectPathRules.IsStateFolderName(name)
                    || string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var childInfo = new DirectoryInfo(child);
                if ((childInfo.Attributes & FileAttributes.ReparsePoint) != 0
                    || childInfo.LinkTarget is not null)
                {
                    continue;
                }

                if (Directory.Exists(Path.Combine(child, ".git"))
                    || File.Exists(Path.Combine(child, ".git")))
                {
                    result.Add(NormalizeGitPath(Path.GetRelativePath(projectRoot, child)));
                    continue;
                }

                pending.Push(child);
            }
        }

        return result;
    }

    private static string EscapeGitGlobPath(string path)
    {
        var builder = new StringBuilder(path.Length);
        foreach (char character in path)
        {
            if (character is '\\' or '*' or '?' or '[' or ']')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string GetProjectPathPrefix(RepositoryInfo repository)
    {
        return repository.Pathspec == "." ? string.Empty : repository.Pathspec + "/";
    }
}
