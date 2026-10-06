using System.Text;
using Beutl.Serialization;

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
        string canonicalProjectRoot = RepositoryPathComparer.ResolveCanonicalPath(repository.ProjectRoot);
        string fullPath = Path.GetFullPath(projectFile);
        string canonicalParent = RepositoryPathComparer.ResolveCanonicalPath(
            Path.GetDirectoryName(fullPath) ?? fullPath);
        // Containment is decided on canonical paths compared ordinally, like every other repository
        // check; a relative path could compare the two parents by platform case rules instead. An
        // entry whose parent sits outside the project root can only be tracked as its resolved target.
        string trackedProjectFile = RepositoryPathComparer.IsContainedWithin(canonicalProjectRoot, canonicalParent)
            ? ResolveTrackedProjectEntry(canonicalParent, Path.GetFileName(fullPath))
            : RepositoryPathComparer.ResolveCanonicalPath(projectFile);
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
            return VersionControlPathComparison.SelectCanonicalExistingEntry(
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

    private static bool AreSameProjectRelativePath(
        string projectRoot,
        string left,
        string right)
    {
        string leftPath = Path.Combine(
            projectRoot,
            left.Replace('/', Path.DirectorySeparatorChar));
        string rightPath = Path.Combine(
            projectRoot,
            right.Replace('/', Path.DirectorySeparatorChar));
        return VersionControlPathComparison.AreSameCanonicalPath(leftPath, rightPath);
    }

    private async Task<string?> FindIgnoredRequiredProjectPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string prefix = GetProjectPathPrefix(repository);
        var nestedRepositories = new List<string>();
        var paths = GetRequiredProjectRelativePaths(repository.ProjectRoot, nestedRepositories)
            .Where(static path => !IsTemporaryProjectFile(path))
            .Select(path => prefix + path)
            .ToList();
        if (repository.Pathspec != ".")
        {
            paths.Add(repository.Pathspec + "/");
        }

        await ThrowIfNestedRepositoryWouldBeStagedAsync(
                repository,
                runner,
                prefix,
                nestedRepositories,
                environmentOverrides: null,
                cancellationToken)
            .ConfigureAwait(false);

        // Git keeps committing a tracked file whatever the ignore rules say, so only an untracked
        // path can be dropped, the same check a snapshot runs.
        return await FindIgnoredPathAsync(
                repository,
                runner,
                paths,
                environmentOverrides: null,
                includeTrackedFiles: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> FindIgnoredExistingRequiredProjectPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pathspecs = CreateIgnoredRequiredProjectPathspecs(repository);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "ls-files",
                "--others",
                "--ignored",
                "--exclude-standard",
                "-z",
                "--",
                .. pathspecs,
            ],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                MaxStdoutBytes: MaxIgnoredRequiredPathOutputBytes,
                UseLiteralPathspecs: false),
            cancellationToken).ConfigureAwait(false);
        if (result.StdoutTruncated
            || !HasOnlyExcludedBeutlDirectoryWarnings(repository, result.Stderr))
        {
            throw new InvalidOperationException(
                "Git could not safely determine whether required project files are ignored.");
        }

        // Git can still list a nested repository inside an ignored folder, for example when a glob
        // character in the project path stops it from pruning the walk. That entry is a directory
        // Git never stages, not one of the files queried here.
        string? ignoredPath = GitCliRunner.SplitNullSeparated(result.Stdout)
            .FirstOrDefault(static path => !path.EndsWith('/'));
        if (ignoredPath is not null)
        {
            return ignoredPath;
        }

        string prefix = GetProjectPathPrefix(repository);
        return await FindIgnoredPathAsync(
                repository,
                runner,
                GetSerializedProjectRelativePaths(repository.ProjectRoot)
                    .Where(static path => !IsTemporaryProjectFile(path))
                    .Select(path => prefix + path),
                environmentOverrides: null,
                includeTrackedFiles: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool HasOnlyExcludedBeutlDirectoryWarnings(
        RepositoryInfo repository,
        string stderr)
    {
        if (stderr.Length == 0)
        {
            return true;
        }

        if (!stderr.EndsWith('\n'))
        {
            return false;
        }

        const string warningPrefix = "warning: could not open directory '";
        const string pathTerminator = "': ";
        int lineStart = 0;
        while (lineStart < stderr.Length)
        {
            int lineEnd = stderr.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                return false;
            }

            ReadOnlySpan<char> line = stderr.AsSpan(lineStart, lineEnd - lineStart);
            if (!line.IsEmpty && line[^1] == '\r')
            {
                line = line[..^1];
            }

            if (line.IsEmpty
                || !IsExcludedBeutlDirectoryWarning(repository, line, warningPrefix, pathTerminator))
            {
                return false;
            }

            lineStart = lineEnd + 1;
        }

        return true;
    }

    private static bool IsExcludedBeutlDirectoryWarning(
        RepositoryInfo repository,
        ReadOnlySpan<char> line,
        string warningPrefix,
        string pathTerminator)
    {
        if (!line.StartsWith(warningPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = line[warningPrefix.Length..];
        int terminatorIndex = remainder.IndexOf(pathTerminator, StringComparison.Ordinal);
        if (terminatorIndex <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> warningPath = remainder[..terminatorIndex];
        ReadOnlySpan<char> reason = remainder[(terminatorIndex + pathTerminator.Length)..];
        if (reason.IsEmpty
            || warningPath.Length < 2
            || warningPath[^1] != '/'
            || warningPath[0] == '/')
        {
            return false;
        }

        warningPath = warningPath[..^1];
        foreach (char character in warningPath)
        {
            if (character is '\'' or '"' or '\\' || char.IsControl(character))
            {
                return false;
            }
        }

        if (reason.Trim().IsEmpty)
        {
            return false;
        }

        foreach (char character in reason)
        {
            if (character is '\'' or '"' or '\\' || char.IsControl(character))
            {
                return false;
            }
        }

        ReadOnlySpan<char> projectPath = repository.Pathspec.AsSpan();
        if (repository.Pathspec != "."
            && (warningPath.Length <= projectPath.Length
                || !warningPath[..projectPath.Length].Equals(projectPath, StringComparison.Ordinal)
                || warningPath[projectPath.Length] != '/'))
        {
            return false;
        }

        ReadOnlySpan<char> relativePath = repository.Pathspec == "."
            ? warningPath
            : warningPath[(projectPath.Length + 1)..];
        int componentStart = 0;
        bool isInBeutlStateDirectory = false;
        while (componentStart < relativePath.Length)
        {
            int separator = relativePath[componentStart..].IndexOf('/');
            int componentLength = separator < 0
                ? relativePath.Length - componentStart
                : separator;
            ReadOnlySpan<char> component = relativePath.Slice(componentStart, componentLength);
            if (component.IsEmpty || component.SequenceEqual(".") || component.SequenceEqual(".."))
            {
                return false;
            }

            isInBeutlStateDirectory |= component.Equals(
                ".beutl",
                StringComparison.OrdinalIgnoreCase);
            if (separator < 0)
            {
                return isInBeutlStateDirectory;
            }

            componentStart += componentLength + 1;
        }

        return false;
    }

    private static IReadOnlyList<string> CreateIgnoredRequiredProjectPathspecs(
        RepositoryInfo repository)
    {
        string prefix = repository.Pathspec == "."
            ? string.Empty
            : EscapeGitGlobPath(repository.Pathspec) + "/";
        // Only the project-root files, with no exclude pathspecs: a pathspec that starts with a
        // wildcard keeps Git from pruning its walk, so it would open every ignored folder and fail
        // closed on one this account cannot read.
        var result = new List<string>(s_ignoredRequiredProjectPathspecSuffixes.Length);
        foreach (string suffix in s_ignoredRequiredProjectPathspecSuffixes)
        {
            result.Add($":(top,glob){prefix}{suffix}");
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

    private async Task<string?> FindIgnoredRequiredProjectPathBeforeInitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repository.ProjectRoot))
        {
            return null;
        }

        string probeRoot = CreateUniqueTempPath("beutl-git-ignore");
        Directory.CreateDirectory(probeRoot);
        try
        {
            var probeRepository = new RepositoryInfo(probeRoot, probeRoot);
            // The probe's branch name is never used, and an invalid init.defaultBranch makes a plain
            // git init fail before the project's own repository is created.
            await runner.RunAsync(
                probeRepository,
                ["-c", "init.defaultBranch=main", "init"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            var environmentOverrides = new Dictionary<string, string?>
            {
                ["GIT_DIR"] = Path.Combine(probeRoot, ".git"),
                ["GIT_WORK_TREE"] = repository.ProjectRoot,
            };
            var nestedRepositories = new List<string>();
            IReadOnlyList<string> requiredPaths = GetRequiredProjectRelativePaths(
                repository.ProjectRoot,
                nestedRepositories);
            await ThrowIfNestedRepositoryWouldBeStagedAsync(
                    probeRepository,
                    runner,
                    prefix: string.Empty,
                    nestedRepositories,
                    environmentOverrides,
                    cancellationToken)
                .ConfigureAwait(false);
            return await FindIgnoredPathAsync(
                    probeRepository,
                    runner,
                    requiredPaths
                        .Where(static path => !IsTemporaryProjectFile(path)),
                    environmentOverrides,
                    includeTrackedFiles: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteIgnoreProbeDirectory(probeRoot);
        }
    }

    private static async Task ThrowIfNestedRepositoryWouldBeStagedAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string prefix,
        IReadOnlyList<string> nestedRepositories,
        IReadOnlyDictionary<string, string?>? environmentOverrides,
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
                    EnvironmentOverrides: environmentOverrides,
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

        // An unignored repository would become a gitlink in the first snapshot, so refuse it before
        // anything is written rather than after.
        string? staged = nestedRepositories.FirstOrDefault(path => !ignored.Contains(prefix + path));
        if (staged is not null)
        {
            throw new InvalidOperationException(
                $"The nested Git repository '{staged}' cannot be snapshotted safely.");
        }
    }

    private static async Task<string?> FindIgnoredPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IEnumerable<string> paths,
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        bool includeTrackedFiles,
        CancellationToken cancellationToken)
    {
        string input = string.Join(
            '\0',
            paths.Distinct(StringComparer.Ordinal)) + '\0';
        if (input.Length == 1)
        {
            return null;
        }

        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                includeTrackedFiles
                    ? ["check-ignore", "--no-index", "--stdin", "-z"]
                    : ["check-ignore", "--stdin", "-z"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    EnvironmentOverrides: environmentOverrides,
                    StandardInput: input,
                    UseLiteralPathspecs: false),
                cancellationToken).ConfigureAwait(false);
            return GitCliRunner.SplitNullSeparated(result.Stdout).FirstOrDefault();
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }

    private IReadOnlyList<string> GetRequiredProjectRelativePaths(
        string projectRoot,
        ICollection<string>? unreferencedNestedRepositories = null)
    {
        IReadOnlySet<string> serializedPaths = GetSerializedProjectRelativePaths(projectRoot);
        // Beutl writes the hygiene files itself. Anything else is required only when the project
        // references it, so a rule ignoring files the project does not use blocks nothing.
        var paths = new HashSet<string>(StringComparer.Ordinal)
        {
            ".gitignore",
            ".gitattributes",
        };

        if (Directory.Exists(projectRoot))
        {
            foreach (string path in EnumerateRequiredProjectFiles(
                         projectRoot,
                         serializedPaths,
                         unreferencedNestedRepositories))
            {
                paths.Add(NormalizeGitPath(Path.GetRelativePath(projectRoot, path)));
            }
        }

        paths.UnionWith(serializedPaths);

        return [.. paths];
    }

    private IReadOnlySet<string> GetSerializedProjectRelativePaths(string projectRoot)
    {
        if (_projectFile is null || !File.Exists(_projectFile))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        string serializationRoot = GetSerializationRoot(_projectFile, projectRoot);
        return SerializedProjectGraph.GetRelativePaths(_projectFile, serializationRoot);
    }

    private IReadOnlySet<string> GetSerializedFileSourceRelativePaths(string projectRoot)
    {
        if (_projectFile is null || !File.Exists(_projectFile))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        string serializationRoot = GetSerializationRoot(_projectFile, projectRoot);
        return SerializedProjectGraph.GetFileSourceRelativePaths(
            _projectFile,
            serializationRoot);
    }

    // The project file's directory when it is the project root, so serialized paths keep its spelling.
    private static string GetSerializationRoot(string projectFile, string projectRoot)
    {
        string projectFileDirectory = Path.GetDirectoryName(projectFile)
                                      ?? throw new InvalidOperationException(
                                          "The project file has no parent directory.");
        return VersionControlPathComparison.AreSameCanonicalPath(
            projectFileDirectory,
            projectRoot)
            ? projectFileDirectory
            : projectRoot;
    }

    private static bool IsTemporaryProjectFile(string path)
    {
        return path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProjectPathPrefix(RepositoryInfo repository)
    {
        return repository.Pathspec == "." ? string.Empty : repository.Pathspec + "/";
    }

    private void ValidateRequiredProjectFileLayout(string projectRoot)
    {
        IReadOnlySet<string> serializedPaths = GetSerializedProjectRelativePaths(projectRoot);
        _requiredTemporaryProjectPaths = serializedPaths
            .Where(static path => IsTemporaryProjectFile(path))
            .ToHashSet(StringComparer.Ordinal);
        _watcher?.UpdateRequiredPaths(serializedPaths);
        foreach (string _ in EnumerateRequiredProjectFiles(projectRoot, serializedPaths))
        {
        }
    }

    private static IEnumerable<string> EnumerateRequiredProjectFiles(
        string projectRoot,
        IReadOnlySet<string> serializedPaths,
        ICollection<string>? unreferencedNestedRepositories = null)
    {
        var pending = new Stack<string>();
        pending.Push(projectRoot);
        // Ordinal, not the platform rule: this dedupes directories the walk actually reached, and
        // a case-sensitive volume can hold both Assets/ and assets/ as distinct trees. Folding them
        // together would skip one subtree's symlink and nested-repository validation entirely.
        var visitedDirectories = new HashSet<string>(StringComparer.Ordinal);
        var options = new EnumerationOptions { AttributesToSkip = 0 };
        while (pending.TryPop(out string? directory))
        {
            string canonicalDirectory = RepositoryPathComparer.ResolveCanonicalPath(directory);
            if (!visitedDirectories.Add(canonicalDirectory))
            {
                continue;
            }

            string relativeDirectoryPath = NormalizeGitPath(Path.GetRelativePath(projectRoot, directory));
            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(directory, "*", options);
                children = Directory.GetDirectories(directory, "*", options);
            }
            catch (Exception ex)
                when (CanSkipUnlistedDirectory(ex, relativeDirectoryPath, serializedPaths))
            {
                continue;
            }

            foreach (string file in files)
            {
                string relativeFile = NormalizeGitPath(Path.GetRelativePath(projectRoot, file));
                // Only a reference makes a file required. An unreferenced file is not project state,
                // whatever its folder or type, so neither an ignore rule nor a link can lose anything
                // the project needs.
                if (serializedPaths.Contains(relativeFile))
                {
                    var fileInfo = new FileInfo(file);
                    fileInfo.Refresh();
                    if (fileInfo.LinkTarget is not null
                        || (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"The required project file symbolic link '{relativeFile}' cannot be snapshotted safely.");
                    }

                    yield return file;
                }
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(child));
                if (!string.Equals(
                        name,
                        ".beutl",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        name,
                        ".git",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var childInfo = new DirectoryInfo(child);
                    childInfo.Refresh();
                    bool isReparsePoint = (childInfo.Attributes & FileAttributes.ReparsePoint) != 0
                                          || childInfo.LinkTarget is not null;
                    string relativeDirectory = NormalizeGitPath(Path.GetRelativePath(
                        projectRoot,
                        child));
                    if (isReparsePoint)
                    {
                        if (serializedPaths.Any(path =>
                                IsSameOrDescendantGitPath(path, relativeDirectory)))
                        {
                            throw new InvalidOperationException(
                                $"The required project content beneath symbolic-link directory '{relativeDirectory}' cannot be snapshotted safely.");
                        }

                        // Never enumerate an unreferenced link target. Besides avoiding an
                        // unbounded or inaccessible external walk, this keeps unrelated content
                        // outside the project from influencing snapshot validation.
                        continue;
                    }

                    if (Directory.Exists(Path.Combine(child, ".git"))
                        || File.Exists(Path.Combine(child, ".git")))
                    {
                        if (serializedPaths.Any(path =>
                                IsSameOrDescendantGitPath(path, relativeDirectory)))
                        {
                            throw new InvalidOperationException(
                                $"The nested Git repository '{relativeDirectory}' cannot be snapshotted safely.");
                        }

                        // Git leaves an ignored repository out of a snapshot and records an unignored
                        // one as a gitlink, which the snapshot tree check refuses. A caller that must
                        // refuse before changing anything asks Git which of these it would stage.
                        unreferencedNestedRepositories?.Add(relativeDirectory);
                        continue;
                    }

                    pending.Push(child);
                }
            }
        }
    }

    // Git warns about a folder it cannot list, whether it is unreadable or vanished or failed while the
    // walk ran, and snapshots everything else. Nothing the project references is inside such a
    // folder, so there is nothing to protect.
    internal static bool CanSkipUnlistedDirectory(
        Exception exception,
        string relativeDirectoryPath,
        IReadOnlySet<string> serializedPaths)
    {
        return exception is UnauthorizedAccessException or IOException
               && relativeDirectoryPath != "."
               && !serializedPaths.Any(path =>
                   IsSameOrDescendantGitPath(path, relativeDirectoryPath));
    }

    private static bool IsSameOrDescendantGitPath(string path, string directory)
    {
        return string.Equals(path, directory, StringComparison.Ordinal)
               || path.StartsWith(directory + "/", StringComparison.Ordinal);
    }

    private void ValidateProjectSnapshotLayout(string projectRoot)
    {
        ValidateRequiredProjectFileLayout(projectRoot);
        if (_projectFile is null || !File.Exists(_projectFile))
        {
            return;
        }

        ValidateNoReservedProjectReferences(_projectFile);
    }

    private static void ValidateNoReservedProjectReferences(string projectFile)
    {
        Project project = CoreSerializer.RestoreFromUri<Project>(new Uri(projectFile));
        VersionControlSerializationGraph.SerializationGraph graph =
            VersionControlSerializationGraph.DiscoverSerializationGraph(project);
        string projectDirectory = Path.GetDirectoryName(projectFile)
                                  ?? throw new InvalidOperationException(
                                      "The project file has no parent directory.");
        Uri? reservedReference = graph.Objects
            .Select(static obj => obj.Uri)
            .Concat(graph.UnaddressableFileSources)
            .Concat(graph.AddressableFileSources)
            .FirstOrDefault(uri => uri is not null
                                   && VersionControlSerializationGraph.IsInReservedProjectPath(
                                       uri,
                                       projectDirectory));
        if (reservedReference is not null)
        {
            string relativePath = NormalizeGitPath(Path.GetRelativePath(
                projectDirectory,
                reservedReference.LocalPath));
            throw new InvalidOperationException(
                $"The required project path '{relativePath}' is beneath a reserved state directory.");
        }
    }

    private static void TryDeleteIgnoreProbeDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ThrowIfRequiredProjectPathIgnored(string? path)
    {
        if (path is not null)
        {
            throw new InvalidOperationException(
                $"The required project path '{path}' is ignored by the repository. "
                + "Update the repository's ignore rules before enabling version control.");
        }
    }
}
