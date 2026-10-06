using System.Formats.Tar;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private async Task<IReadOnlySet<string>> GetRequiredTemporaryProjectPathsAtCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        CancellationToken cancellationToken)
    {
        if (_historicalRequiredTemporaryPaths.TryGetValue(
                commit,
                out IReadOnlySet<string>? cachedPaths))
        {
            return cachedPaths;
        }

        if (_projectFile is null
            || !RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, _projectFile))
        {
            return CacheHistoricalRequiredTemporaryPaths(commit, []);
        }

        if (!await CommitHasTrackedTemporaryPathsAsync(
                repository,
                runner,
                commit,
                cancellationToken).ConfigureAwait(false))
        {
            return CacheHistoricalRequiredTemporaryPaths(commit, []);
        }

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"beutl-historical-graph-{Guid.NewGuid():N}");
        string materializedRepositoryRoot = Path.Combine(temporaryRoot, "tree");
        try
        {
            Directory.CreateDirectory(materializedRepositoryRoot);
            string projectFileRepositoryPath = GetRepositoryRelativeProjectFilePath(
                repository,
                _projectFile);
            Dictionary<string, long> graphFiles = await ListHistoricalGraphFilesAsync(
                    repository,
                    runner,
                    commit,
                    projectFileRepositoryPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!graphFiles.ContainsKey(projectFileRepositoryPath))
            {
                return CacheHistoricalRequiredTemporaryPaths(commit, []);
            }

            await MaterializeHistoricalGraphFilesAsync(
                    repository,
                    runner,
                    commit,
                    materializedRepositoryRoot,
                    graphFiles,
                    cancellationToken)
                .ConfigureAwait(false);

            string materializedProjectRoot = repository.Pathspec == "."
                ? materializedRepositoryRoot
                : GetMaterializedHistoricalPath(
                    materializedRepositoryRoot,
                    repository.Pathspec);
            string materializedProjectFile = GetMaterializedHistoricalPath(
                materializedRepositoryRoot,
                projectFileRepositoryPath);
            ValidateNoReservedProjectReferences(materializedProjectFile);
            IReadOnlySet<string> serializedPaths = SerializedProjectGraph.GetRelativePaths(
                materializedProjectFile,
                materializedProjectRoot);
            HashSet<string> requiredTemporaryPaths = serializedPaths
                .Where(static path => path.EndsWith(
                    ".tmp",
                    StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.Ordinal);
            return CacheHistoricalRequiredTemporaryPaths(commit, requiredTemporaryPaths);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                   and not OperationCanceledException)
        {
            LogWarningBestEffort(
                ex,
                "The base commit's serialized project graph could not be read safely; previously required temporary files will be retained.");
            return new HashSet<string>(StringComparer.Ordinal);
        }
        finally
        {
            TryDeleteHistoricalGraphDirectory(temporaryRoot);
        }
    }

    private async Task<bool> CommitHasTrackedTemporaryPathsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                [
                    "ls-tree",
                    "-r",
                    "-z",
                    "--name-only",
                    commit,
                    "--",
                    CreateSnapshotBasePathspec(repository),
                ],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    MaxStdoutBytes: MaxHistoricalGraphListBytes,
                    UseLiteralPathspecs: false),
                cancellationToken)
            .ConfigureAwait(false);
        return result.StdoutTruncated
               || GitCliRunner.SplitNullSeparated(result.Stdout)
                   .Any(static path => path.EndsWith(
                       ".tmp",
                       StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlySet<string> CacheHistoricalRequiredTemporaryPaths(
        string commit,
        IEnumerable<string> paths)
    {
        if (_historicalRequiredTemporaryPaths.Count >= MaxHistoricalGraphCacheEntries)
        {
            string oldest = _historicalRequiredTemporaryPaths.Keys.First();
            _historicalRequiredTemporaryPaths.Remove(oldest);
        }

        var snapshot = new HashSet<string>(paths, StringComparer.Ordinal);
        _historicalRequiredTemporaryPaths[commit] = snapshot;
        return snapshot;
    }

    private static async Task<Dictionary<string, long>> ListHistoricalGraphFilesAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        string projectFileRepositoryPath,
        CancellationToken cancellationToken)
    {
        GitCommandResult listed = await runner.RunAsync(
                repository,
                [
                    "ls-tree",
                    "-r",
                    "-z",
                    "--long",
                    commit,
                    "--",
                    CreateSnapshotBasePathspec(repository),
                ],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    MaxStdoutBytes: MaxHistoricalGraphListBytes,
                    UseLiteralPathspecs: false),
                cancellationToken)
            .ConfigureAwait(false);
        if (listed.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The base commit's serialized project file list exceeded its safety limit.");
        }

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (string record in GitCliRunner.SplitNullSeparated(listed.Stdout))
        {
            int separator = record.IndexOf('\t');
            string[] metadata = separator < 0
                ? []
                : record[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string path = separator < 0 ? string.Empty : record[(separator + 1)..];
            bool isSerializedGraphFile = string.Equals(
                                             path,
                                             projectFileRepositoryPath,
                                             StringComparison.Ordinal)
                                         || Path.GetExtension(path) is { } extension
                                         && (string.Equals(
                                                 extension,
                                                 ".scene",
                                                 StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(
                                                 extension,
                                                 ".belm",
                                                 StringComparison.OrdinalIgnoreCase));
            if (!isSerializedGraphFile)
            {
                continue;
            }

            if (metadata.Length != 4
                || metadata[1] != "blob"
                || metadata[0] is not ("100644" or "100755")
                || !long.TryParse(metadata[3], out long size)
                || size < 0
                || !IsSafeHistoricalGraphPath(repository, path)
                || !result.TryAdd(path, size))
            {
                throw new InvalidOperationException(
                    "The base commit contains an unsafe serialized project graph entry.");
            }

            totalBytes = checked(totalBytes + size);
            if (result.Count > MaxHistoricalGraphFileCount
                || totalBytes > MaxHistoricalGraphBytes)
            {
                throw new InvalidOperationException(
                    "The base commit's serialized project graph exceeded its safety limit.");
            }
        }

        return result;
    }

    private static async Task MaterializeHistoricalGraphFilesAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        string destinationRoot,
        IReadOnlyDictionary<string, long> graphFiles,
        CancellationToken cancellationToken)
    {
        int archiveIndex = 0;
        foreach (IReadOnlyList<string> batch in BatchHistoricalGraphPaths(graphFiles.Keys))
        {
            string archivePath = Path.Combine(
                Path.GetDirectoryName(destinationRoot)!,
                $"graph-{archiveIndex++}.tar");
            await runner.RunAsync(
                    repository,
                    [
                        "archive",
                        "--format=tar",
                        $"--output={archivePath}",
                        $"{commit}^{{tree}}",
                        "--",
                        .. batch,
                    ],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            var expectedBatch = batch.ToDictionary(
                static path => path,
                path => graphFiles[path],
                StringComparer.Ordinal);
            await ExtractHistoricalGraphArchiveAsync(
                    archivePath,
                    destinationRoot,
                    expectedBatch,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static IEnumerable<IReadOnlyList<string>> BatchHistoricalGraphPaths(
        IEnumerable<string> paths)
    {
        var batch = new List<string>();
        int batchCharacters = 0;
        foreach (string path in paths)
        {
            if (batch.Count > 0
                && batchCharacters + path.Length > MaxHistoricalArchivePathspecCharacters)
            {
                yield return batch;
                batch = [];
                batchCharacters = 0;
            }

            batch.Add(path);
            batchCharacters += path.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static bool IsSafeHistoricalGraphPath(
        RepositoryInfo repository,
        string repositoryRelativePath)
    {
        if (!TryGetSafeHistoricalPathComponents(repositoryRelativePath, out _))
        {
            return false;
        }

        string path = Path.Combine(
            repository.RepoRoot,
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, path);
    }

    private static async Task ExtractHistoricalGraphArchiveAsync(
        string archivePath,
        string destinationRoot,
        IReadOnlyDictionary<string, long> expectedFiles,
        CancellationToken cancellationToken)
    {
        await using var archive = new FileStream(
            archivePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        await using var reader = new TarReader(archive, leaveOpen: false);
        var extractedFiles = new HashSet<string>(StringComparer.Ordinal);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(
                   copyData: false,
                   cancellationToken).ConfigureAwait(false)) is not null)
        {
            string entryName = entry.EntryType == TarEntryType.Directory
                ? entry.Name.TrimEnd('/')
                : entry.Name;
            if (!TryGetSafeHistoricalPathComponents(entryName, out string[] components))
            {
                throw new InvalidOperationException(
                    "The base commit archive contains an unsafe path.");
            }

            string destination = Path.Combine([destinationRoot, .. components]);
            if (!RepositoryPathComparer.IsContainedWithin(destinationRoot, destination))
            {
                throw new InvalidOperationException(
                    "The base commit archive escaped its materialization root.");
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                || !expectedFiles.TryGetValue(entryName, out long expectedLength)
                || entry.Length != expectedLength
                || entry.DataStream is null
                || !extractedFiles.Add(entryName))
            {
                throw new InvalidOperationException(
                    "The base commit archive did not match its validated file list.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var output = new FileStream(
                destination,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                });
            await entry.DataStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (output.Length != expectedLength)
            {
                throw new InvalidOperationException(
                    "The base commit archive entry changed length during extraction.");
            }
        }

        if (!extractedFiles.SetEquals(expectedFiles.Keys))
        {
            throw new InvalidOperationException(
                "The base commit archive omitted a serialized project graph file.");
        }
    }

    private static bool TryGetSafeHistoricalPathComponents(
        string path,
        out string[] components)
    {
        components = path.Split('/');
        return !string.IsNullOrEmpty(path)
               && path[0] != '/'
               && (!OperatingSystem.IsWindows() || !path.Contains('\\'))
               && components.All(static component => component is not ("" or "." or ".."));
    }

    private static string GetMaterializedHistoricalPath(string root, string gitPath)
    {
        if (!TryGetSafeHistoricalPathComponents(gitPath, out string[] components))
        {
            throw new InvalidOperationException(
                "The historical project path is unsafe to materialize.");
        }

        string result = Path.Combine([root, .. components]);
        if (!RepositoryPathComparer.IsContainedWithin(root, result))
        {
            throw new InvalidOperationException(
                "The historical project path escaped its materialization root.");
        }

        return result;
    }

    private static void TryDeleteHistoricalGraphDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
