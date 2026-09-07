using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.VersionControl;

internal sealed class GitCliVersionControlService :
    IProjectVersionControlBackend
{
    private const int PendingPullRecoveryFormatVersion = 1;
    private const int MaxPendingRecoveryListBytes = 1024 * 1024;
    private const int MaxPendingRecoveryDescriptorBytes = 64 * 1024;
    private const int MaxHistoricalGraphListBytes = 4 * 1024 * 1024;
    private const int MaxHistoricalGraphFileCount = 16 * 1024;
    private const long MaxHistoricalGraphBytes = 128L * 1024 * 1024;
    private const int MaxHistoricalGraphCacheEntries = 32;
    private const int MaxHistoricalArchivePathspecCharacters = 16 * 1024;
    // A repository can restrict which LFS paths are hydrated (lfs.fetchinclude / lfs.fetchexclude).
    // A transition has to reopen the project on its real media, so its checkout clears those
    // filters: an excluded pointer is copied through unchanged, which would leave pointer text in
    // the work tree where the media belongs. Repository-wide prefetches use the same cleared
    // baseline; project restore narrows the scan with an explicit include or exact subtree.
    private static readonly string[] s_lfsPathFilterOverrides =
    [
        "-c",
        "lfs.fetchinclude=",
        "-c",
        "lfs.fetchexclude=",
    ];

    private static readonly JsonSerializerOptions s_recoveryJsonOptions =
        new(JsonSerializerOptions.Strict);

    private sealed record PendingPullRecoveryData(
        int Version,
        string Id,
        string CheckpointRef,
        string CheckpointCommit,
        string BranchRef,
        string BaseCommit,
        string TargetCommit,
        string ProjectFile,
        DateTimeOffset CreatedAt);

    private sealed record LfsAttributeQueryResult(
        HashSet<string> CoveredPaths,
        bool IsComplete);

    private sealed record LfsPrefetchTarget(
        string Reference,
        IReadOnlyList<string> PathArguments);

    private sealed record WorktreeStateFingerprint(string Tree, string IndexEntries);

    private sealed record IndexFileSnapshot(
        bool Exists,
        byte[] Contents,
        FileAttributes? Attributes,
        UnixFileMode? UnixMode,
        DateTime? LastWriteTimeUtc);

    private sealed record SnapshotTreeCapture(
        string Tree,
        string IndexPath,
        IndexFileSnapshot Index,
        IReadOnlyList<string> TemporaryPathspecsToReconcile);

    private sealed record SnapshotIndexCommandPlan(
        IReadOnlyList<IReadOnlyList<string>> Commands,
        IReadOnlyList<string> TemporaryPathspecsToReconcile);

    private sealed record SnapshotTreeBuildResult(
        string Tree,
        IReadOnlyList<string> TemporaryPathspecsToReconcile);

    private sealed record SnapshotCommit(
        string Commit,
        string Tree,
        string MessagePath,
        SnapshotIdentity Author,
        SnapshotIdentity Committer);

    private sealed record SnapshotIdentity(
        string Name,
        string Email,
        string Date);

    private sealed class IndexRollbackAmbiguousException : InvalidOperationException
    {
        public IndexRollbackAmbiguousException(string message)
            : base(message)
        {
        }

        public IndexRollbackAmbiguousException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private enum TreeTransitionOutcome
    {
        AppliedTarget,
        RestoredCurrent,
        OwnershipLost,
        RecoveryFailed,
    }

    private enum PullRelation
    {
        Equal,
        LocalBehind,
        LocalAhead,
        Diverged,
    }

    private enum CommitCleanupMode
    {
        Whitespace,
        Strip,
        Verbatim,
    }

    private sealed record PullFetchTarget(
        IReadOnlyList<string> Arguments,
        string UpstreamRef);

    private sealed record BranchUpstreamConfiguration(
        string RemoteName,
        string RemoteRef);

    private sealed record TreeTransitionResult(
        TreeTransitionOutcome Outcome,
        Exception? Error = null,
        CheckedOutBranchTip? ActualTip = null);

    private sealed record TreeTransitionIndexPlan(
        string? PrepareCommit = null,
        string? FinalCommit = null,
        string? RestoreCommit = null,
        string Pathspec = ".");

    private sealed class HeadOwnershipLease : IDisposable
    {
        private readonly Action<Exception>? _releaseFailureSink;
        private readonly string _headPath;
        private readonly string _expectedRefName;
        private FileStream? _stream;

        private HeadOwnershipLease(
            string headPath,
            string expectedRefName,
            string lockPath,
            FileStream stream,
            Action<Exception>? releaseFailureSink)
        {
            _headPath = headPath;
            _expectedRefName = expectedRefName;
            LockPath = lockPath;
            _stream = stream;
            _releaseFailureSink = releaseFailureSink;
        }

        public string LockPath { get; }

        public static HeadOwnershipLease Acquire(
            string headPath,
            string expectedRefName,
            Action<Exception>? releaseFailureSink)
        {
            string lockPath = headPath + ".lock";
            FileStream stream;
            try
            {
                stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new GitOperationException(
                    128,
                    $"Unable to acquire the worktree HEAD lock '{lockPath}': {ex.Message}");
            }

            var lease = new HeadOwnershipLease(
                headPath,
                expectedRefName,
                lockPath,
                stream,
                releaseFailureSink);
            try
            {
                lease.VerifyStillOwned();
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public void VerifyStillOwned()
        {
            if (_stream is null)
            {
                throw new ObjectDisposedException(nameof(HeadOwnershipLease));
            }

            string expected = $"ref: {_expectedRefName}\n";
            string actual = File.ReadAllText(_headPath, new UTF8Encoding(false));
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new ProjectCheckpointStateChangedException();
            }
        }

        public void Dispose()
        {
            FileStream? stream = Interlocked.Exchange(ref _stream, null);
            if (stream is null)
            {
                return;
            }

            try
            {
                stream.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _releaseFailureSink?.Invoke(ex);
            }

            try
            {
                File.Delete(LockPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _releaseFailureSink?.Invoke(ex);
            }
        }
    }

    internal const int MaxDiffBytes = 1024 * 1024;
    internal const string DiffTruncationMarker = "\n--- Diff truncated at 1 MB ---\n";
    private const string OriginRefPrefix = "refs/remotes/origin/";
    private const string LfsQuotaNoticeConfigKeyPrefix = "beutl.lfsQuotaNoticeShown-";
    private const string LargeMediaNoticeConfigKeyPrefix = "beutl.largeMediaNoticeShown-";
    private const string MissingIdentityNoticeConfigKeyPrefix = "beutl.missingIdentityNoticeShown-";
    private const string PullSafetyCommitMessage = "beutl: safety snapshot before pull";
    private const string ManagedLfsBeginMarker = "# BEGIN BEUTL MANAGED LFS";
    private const string ManagedLfsEndMarker = "# END BEUTL MANAGED LFS";
    private const int MaxHygieneWriteAttempts = 3;
    private const int MaxIgnoredRequiredPathOutputBytes = 256 * 1024;
    private const int MaxLfsAttributeOutputBytes = 256 * 1024;
    private const int MaxLfsFetchOutputBytes = 64 * 1024;
    private const int MaxLfsObjectListOutputBytes = 4 * 1024 * 1024;
    private const int MaxLfsPointerBytes = 1024;
    private const int MaxLfsPointerCandidates = 256;
    private const int MaxSnapshotTreeInspectionBytes = 4 * 1024 * 1024;
    private const int MaxCommitMessageBytes = 1024 * 1024;

    private static readonly string[] s_gitIgnoreLines =
    [
        "**/.beutl/",
        "*.[tT][mM][pP]",
    ];

    private static readonly string[] s_textAttributeLines =
    [
        "*.[bB][eE][pP] text eol=lf",
        "*.[sS][cC][eE][nN][eE] text eol=lf",
        "*.[bB][eE][lL][mM] text eol=lf",
        ".gitignore text eol=lf",
        ".gitattributes text eol=lf",
    ];

    // Stable union of the existing policy, Engine built-in decoders, the FFmpeg and
    // MF/AVF decoders, and SharedFilePickerOptions.OpenImage. Do not derive this from
    // DecoderRegistry: repository attributes must not vary with platform or extension load state.
    private static readonly string[] s_supportedMediaExtensions =
    [
        ".mp4",
        ".mov",
        ".mkv",
        ".avi",
        ".wmv",
        ".flv",
        ".webm",
        ".wav",
        ".mp3",
        ".flac",
        ".aac",
        ".m4a",
        ".ogg",
        ".opus",
        ".wma",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".webp",
        ".tiff",
        ".tif",
        // Engine built-in decoder additions.
        ".wave",
        ".apng",
        // FFmpeg decoder additions.
        ".264",
        ".mpeg",
        ".ts",
        ".mts",
        ".m2ts",
        // Media Foundation and AVFoundation decoder additions.
        ".sami",
        ".smi",
        ".m4v",
        ".adts",
        ".asf",
        ".3gp",
        ".3gp2",
        ".3gpp",
        // SharedFilePickerOptions.OpenImage additions.
        ".ico",
        ".wbmp",
        ".pkm",
        ".ktx",
        ".astc",
        ".dng",
        ".heif",
        ".avif",
    ];

    private static readonly string[] s_lfsAttributeLines =
        s_supportedMediaExtensions
            .Select(static extension =>
                $"**/*{CreateCaseInsensitiveGlob(extension)} "
                + "filter=lfs diff=lfs merge=lfs -text")
            .ToArray();

    private static readonly HashSet<string> s_mediaExtensions = new(
        s_supportedMediaExtensions,
        StringComparer.OrdinalIgnoreCase);

    internal static bool IsSupportedMediaPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return s_mediaExtensions.Contains(Path.GetExtension(path));
    }

    private static readonly HashSet<string> s_projectFileExtensions = new(
        [".bep", ".scene", ".belm"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] s_ignoredRequiredProjectPathspecSuffixes =
    [
        "**/*.[bB][eE][pP]",
        "**/*.[sS][cC][eE][nN][eE]",
        "**/*.[bB][eE][lL][mM]",
        "**/[rR][eE][sS][oO][uU][rR][cC][eE][sS]/**",
        ".gitignore",
        ".gitattributes",
    ];

    private const string TemporaryFilePathspecSuffix = "**/*.[tT][mM][pP]";

    private static readonly string[] s_ignoredOptionalProjectPathspecSuffixes =
    [
        "**/.[bB][eE][uU][tT][lL]/**",
        TemporaryFilePathspecSuffix,
    ];

    private IReadOnlyList<string> CreateSnapshotExcludePathspecs(RepositoryInfo repository)
    {
        string prefix = repository.Pathspec == "."
            ? string.Empty
            : EscapeGitGlobPath(repository.Pathspec) + "/";
        // Broad staging always excludes `.tmp` scratch files. Serialized `.tmp` sidecars are
        // added by exact literal path through CreateSnapshotIndexCommands so one required sidecar
        // never widens the snapshot to every temporary file in the project.
        return s_ignoredOptionalProjectPathspecSuffixes
            .Select(suffix => $":(top,exclude,glob){prefix}{suffix}")
            .ToArray();
    }

    private async Task<SnapshotIndexCommandPlan> CreateSnapshotIndexCommandsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string? baseCommit,
        CancellationToken cancellationToken)
    {
        var addArguments = new List<string>
        {
            "-c",
            "advice.addIgnoredFile=false",
            "add",
            "-A",
            "--",
            CreateSnapshotBasePathspec(repository),
        };
        addArguments.AddRange(CreateSnapshotExcludePathspecs(repository));

        var commands = new List<IReadOnlyList<string>> { addArguments };
        IReadOnlyList<string> requiredTemporaryPathspecs =
            GetRequiredTemporaryRepositoryPathspecs(repository);
        IReadOnlySet<string> previousRequiredTemporaryPaths = baseCommit is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await GetRequiredTemporaryProjectPathsAtCommitAsync(
                    repository,
                    runner,
                    baseCommit,
                    cancellationToken)
                .ConfigureAwait(false);
        string[] noLongerRequiredPathspecs = previousRequiredTemporaryPaths
            .Where(previousPath => !_requiredTemporaryProjectPaths.Any(
                currentPath => AreSameProjectRelativePath(
                    repository.ProjectRoot,
                    previousPath,
                    currentPath)))
            .Select(path => CreateRequiredTemporaryPathspec(repository, path))
            .ToArray();
        string[] pathspecsToRemove = requiredTemporaryPathspecs
            .Concat(noLongerRequiredPathspecs)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (pathspecsToRemove.Length > 0)
        {
            // Removing current paths refreshes their blob or records a physical deletion. Removing
            // paths required by the base graph but not the current graph prevents a dereferenced
            // sidecar from leaking into the next tree. Unrelated tracked scratch files stay in the
            // base tree and are not widened into the snapshot.
            commands.Add(
            [
                "--literal-pathspecs",
                "update-index",
                "--force-remove",
                "--",
                .. pathspecsToRemove.Select(GetRepositoryPathFromLiteralPathspec),
            ]);
        }

        string[] existingPathspecs = requiredTemporaryPathspecs
            .Where(pathspec => File.Exists(GetProjectPathFromLiteralPathspec(repository, pathspec)))
            .ToArray();
        if (existingPathspecs.Length > 0)
        {
            commands.Add(
            [
                "-c",
                "advice.addIgnoredFile=false",
                "add",
                "-A",
                "-f",
                "--",
                .. existingPathspecs,
            ]);
        }

        return new SnapshotIndexCommandPlan(commands, pathspecsToRemove);
    }

    private IReadOnlyList<IReadOnlyList<string>> CreateSnapshotIndexReconciliationCommands(
        RepositoryInfo repository,
        string commit,
        IReadOnlyList<string> temporaryPathspecsToReconcile)
    {
        var resetProject = new List<string>
        {
            "reset",
            "-q",
            commit,
            "--",
            CreateSnapshotBasePathspec(repository),
        };
        resetProject.AddRange(CreateSnapshotExcludePathspecs(repository));

        var commands = new List<IReadOnlyList<string>> { resetProject };
        if (temporaryPathspecsToReconcile.Count > 0)
        {
            commands.Add(
            [
                "reset",
                "-q",
                commit,
                "--",
                .. temporaryPathspecsToReconcile,
            ]);
        }

        return commands;
    }

    private IReadOnlyList<string> GetRequiredTemporaryRepositoryPathspecs(
        RepositoryInfo repository)
    {
        return _requiredTemporaryProjectPaths
            .Select(path => CreateRequiredTemporaryPathspec(repository, path))
            .ToArray();
    }

    private static string CreateRequiredTemporaryPathspec(
        RepositoryInfo repository,
        string projectRelativePath)
    {
        string prefix = repository.Pathspec == "." ? string.Empty : repository.Pathspec + "/";
        return $":(top,literal){prefix}{projectRelativePath}";
    }

    private static string GetRepositoryRelativeProjectFilePath(
        RepositoryInfo repository,
        string projectFile)
    {
        string projectRelativePath = NormalizeGitPath(Path.GetRelativePath(
            RepositoryPathComparer.ResolveCanonicalPath(repository.ProjectRoot),
            RepositoryPathComparer.ResolveCanonicalPath(projectFile)));
        return repository.Pathspec == "."
            ? projectRelativePath
            : repository.Pathspec + "/" + projectRelativePath;
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

    private static string GetProjectPathFromLiteralPathspec(
        RepositoryInfo repository,
        string pathspec)
    {
        const string literalPrefix = ":(top,literal)";
        string repositoryRelativePath = pathspec[literalPrefix.Length..];
        string projectRelativePath = repository.Pathspec == "."
            ? repositoryRelativePath
            : repositoryRelativePath[(repository.Pathspec.Length + 1)..];
        return Path.Combine(
            repository.ProjectRoot,
            projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetRepositoryPathFromLiteralPathspec(string pathspec)
    {
        const string literalPrefix = ":(top,literal)";
        return pathspec[literalPrefix.Length..];
    }

    private static string CreateSnapshotBasePathspec(RepositoryInfo repository)
    {
        return repository.Pathspec == "."
            ? "."
            : $":(top,literal){repository.Pathspec}";
    }

    private static readonly string[] s_repositoryOperationRefs =
    [
        "MERGE_HEAD",
        "CHERRY_PICK_HEAD",
        "REVERT_HEAD",
        "rebase-merge",
        "rebase-apply",
        "sequencer",
    ];

    private static string CreateCaseInsensitiveGlob(string value)
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

    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<string, IGitCliRunner> _runnerFactory;
    private readonly Func<bool> _isWorktreeMutationAllowed;
    private readonly string? _projectFile;
    private readonly Dictionary<string, IReadOnlySet<string>> _historicalRequiredTemporaryPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<VersionControlPolicyNotice, CancellationToken, Task>? _policyNoticeSink;
    private readonly Func<string, CancellationToken, Task>? _beforeHygieneFileReplace;
    private readonly Func<string, CancellationToken, Task>? _beforeFileCommit;
    private readonly Func<string, CancellationToken, Task>? _afterFileExchange;
    private readonly Func<string, bool> _deleteVerifiedHygieneFile;
    private readonly Func<string, bool> _deleteOwnedLocalConfigFile;
    private readonly Action<Action> _statusNotificationScheduler;
    private readonly Action<Action> _lockNotificationScheduler;
    private readonly ILogger _logger;
    private readonly bool _createWatcherWhenRepositoryAvailable;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _lifetimeSync = new();
    private readonly object _runtimeSync = new();
    private readonly ConcurrentQueue<WorkspaceStatus> _statusNotifications = new();
    private IReadOnlySet<string> _requiredTemporaryProjectPaths =
        new HashSet<string>(StringComparer.Ordinal);
    private RepositoryWatcher? _watcher;
    private GitAvailability? _cachedAvailability;
    private IGitCliRunner? _runner;
    private Task<CommitResult?>? _retirementTask;
    private int _configurationRevision;
    private int _lifetimeState;
    private int _resourcesDisposed;
    private int _statusNotificationDrainScheduled;

    public GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository = null)
        : this(
            installationLocator,
            repository,
            repository is null ? null : new RepositoryWatcher(repository),
            static gitPath => new GitCliRunner(gitPath),
            createWatcherWhenRepositoryAvailable: true,
            isWorktreeMutationAllowed: static () => true,
            projectFile: null,
            policyNoticeSink: null,
            beforeHygieneFileReplace: null,
            beforeFileCommit: null,
            afterFileExchange: null,
            deleteVerifiedHygieneFile: null,
            deleteOwnedLocalConfigFile: null,
            statusNotificationScheduler: null,
            lockNotificationScheduler: null,
            logger: null)
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        Func<bool> isWorktreeMutationAllowed,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink = null,
        string? projectFile = null)
        : this(
            installationLocator,
            repository,
            repository is null ? null : new RepositoryWatcher(repository),
            static gitPath => new GitCliRunner(gitPath),
            createWatcherWhenRepositoryAvailable: true,
            isWorktreeMutationAllowed: isWorktreeMutationAllowed,
            projectFile: projectFile,
            policyNoticeSink: policyNoticeSink,
            beforeHygieneFileReplace: null,
            beforeFileCommit: null,
            afterFileExchange: null,
            deleteVerifiedHygieneFile: null,
            deleteOwnedLocalConfigFile: null,
            statusNotificationScheduler: null,
            lockNotificationScheduler: null,
            logger: null)
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        RepositoryWatcher? watcher,
        Func<string, IGitCliRunner> runnerFactory,
        ILogger? logger = null,
        Func<string, CancellationToken, Task>? beforeHygieneFileReplace = null,
        Func<string, CancellationToken, Task>? beforeFileCommit = null,
        Func<string, CancellationToken, Task>? afterFileExchange = null,
        Func<string, bool>? deleteVerifiedHygieneFile = null,
        Func<string, bool>? deleteOwnedLocalConfigFile = null,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink = null,
        Action<Action>? statusNotificationScheduler = null,
        Action<Action>? lockNotificationScheduler = null,
        string? projectFile = null)
        : this(
            installationLocator,
            repository,
            watcher,
            runnerFactory,
            createWatcherWhenRepositoryAvailable: false,
            isWorktreeMutationAllowed: static () => true,
            projectFile: projectFile,
            policyNoticeSink,
            beforeHygieneFileReplace: beforeHygieneFileReplace,
            beforeFileCommit: beforeFileCommit,
            afterFileExchange: afterFileExchange,
            deleteVerifiedHygieneFile: deleteVerifiedHygieneFile,
            deleteOwnedLocalConfigFile: deleteOwnedLocalConfigFile,
            statusNotificationScheduler: statusNotificationScheduler,
            lockNotificationScheduler: lockNotificationScheduler,
            logger: logger)
    {
    }

    private GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        RepositoryWatcher? watcher,
        Func<string, IGitCliRunner> runnerFactory,
        bool createWatcherWhenRepositoryAvailable,
        Func<bool> isWorktreeMutationAllowed,
        string? projectFile,
        Func<VersionControlPolicyNotice, CancellationToken, Task>? policyNoticeSink,
        Func<string, CancellationToken, Task>? beforeHygieneFileReplace,
        Func<string, CancellationToken, Task>? beforeFileCommit,
        Func<string, CancellationToken, Task>? afterFileExchange,
        Func<string, bool>? deleteVerifiedHygieneFile,
        Func<string, bool>? deleteOwnedLocalConfigFile,
        Action<Action>? statusNotificationScheduler,
        Action<Action>? lockNotificationScheduler,
        ILogger? logger)
    {
        _installationLocator = installationLocator
                               ?? throw new ArgumentNullException(nameof(installationLocator));
        if (watcher is not null && repository is null)
        {
            throw new ArgumentException(
                "A watcher can only be supplied for an associated repository.",
                nameof(watcher));
        }

        Repository = repository;
        _watcher = watcher;
        _runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));
        _isWorktreeMutationAllowed = isWorktreeMutationAllowed
                                     ?? throw new ArgumentNullException(
                                         nameof(isWorktreeMutationAllowed));
        _projectFile = projectFile is null ? null : Path.GetFullPath(projectFile);
        _policyNoticeSink = policyNoticeSink;
        _beforeHygieneFileReplace = beforeHygieneFileReplace;
        _beforeFileCommit = beforeFileCommit;
        _afterFileExchange = afterFileExchange;
        _deleteVerifiedHygieneFile = deleteVerifiedHygieneFile
                                     ?? TryDeleteVerifiedHygieneFile;
        _deleteOwnedLocalConfigFile = deleteOwnedLocalConfigFile
                                      ?? DeleteOwnedLocalConfigFile;
        _statusNotificationScheduler = statusNotificationScheduler ?? ScheduleStatusNotificationDrain;
        _lockNotificationScheduler = lockNotificationScheduler ?? ScheduleLockNotification;
        _logger = logger ?? Log.CreateLogger<GitCliVersionControlService>();
        _createWatcherWhenRepositoryAvailable = createWatcherWhenRepositoryAvailable;
        if (_watcher is not null)
        {
            if (repository is not null)
            {
                try
                {
                    IReadOnlySet<string> serializedPaths =
                        GetSerializedProjectRelativePaths(repository.ProjectRoot);
                    _requiredTemporaryProjectPaths = serializedPaths
                        .Where(static path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        .ToHashSet(StringComparer.Ordinal);
                    _watcher.UpdateRequiredPaths(serializedPaths);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    LogWarningBestEffort(
                        ex,
                        "The initial repository watcher paths could not be read from the serialized project graph.");
                }
            }

            _watcher.Changed += OnRepositoryChanged;
        }

        _installationLocator.Config.ConfigurationChanged += OnVersionControlConfigChanged;
    }

    public RepositoryInfo? Repository { get; private set; }

    public RepositoryLockInfo? RecoverableLock { get; private set; }

    public event EventHandler<WorkspaceStatus>? StatusChanged;

    public event EventHandler<RepositoryLockInfo>? RecoverableLockAvailable;

    public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () => (await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false)).Availability,
            cancellationToken);
    }

    public Task<RepositoryInfo?> DiscoverRepositoryAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return RunSerializedAsync(
            async () =>
            {
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await DiscoverRepositoryCoreAsync(projectRoot, runner, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task InitializeAsync(InitOptions options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        return RunSerializedAsync(
            () => InitializeCoreAsync(options, cancellationToken),
            cancellationToken);
    }

    public Task EnsureRepositoryHygieneAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                (GitAvailability availability, IGitCliRunner? runner)
                    = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
                if (availability.State != GitAvailabilityState.Installed || runner is null)
                {
                    throw new InvalidOperationException("Git is not available.");
                }

                await EnsureRepositoryHygienePreflightCoreAsync(
                        repository,
                        runner,
                        cancellationToken)
                    .ConfigureAwait(false);
                bool useLfs = _installationLocator.Config.UseLfsWhenAvailable
                              && availability.LfsInstalled;
                await EnsureRepositoryHygieneCoreAsync(
                        repository,
                        runner,
                        useLfs,
                        cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<bool> HasVersionTrackingOptInAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(repository);
        return RunSerializedAsync(
            () => HasVersionTrackingOptInCoreAsync(repository, cancellationToken),
            cancellationToken);
    }

    public Task<CommitResult> CommitAllAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunSerializedAsync(
            () => CommitAllCoreAsync(message, kind, cancellationToken),
            cancellationToken);
    }

    public Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetCheckedOutBranchTipCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task<PullPreflightResult> PreflightPullAsync(
        CheckedOutBranchTip expectedCurrent,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        return RunSerializedAsync(
            () => PreflightPullCoreAsync(expectedCurrent, cancellationToken),
            cancellationToken);
    }

    public Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
        string message,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunSerializedAsync(
            () => CreateProjectCheckpointCoreAsync(message, cancellationToken),
            cancellationToken);
    }

    public Task<PendingPullRecovery> PersistPendingPullRecoveryAsync(
        ProjectCheckpoint checkpoint,
        CheckedOutBranchTip targetTip,
        string projectFile,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(targetTip);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        return RunSerializedAsync(
            () => PersistPendingPullRecoveryCoreAsync(
                checkpoint,
                targetTip,
                projectFile,
                cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<PendingPullRecovery>> GetPendingPullRecoveriesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetPendingPullRecoveriesCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task<PendingPullRecoveryOutcome> RecoverPendingPullRecoveryAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(recovery);
        return RunSerializedAsync(
            () => RecoverPendingPullRecoveryCoreAsync(recovery, cancellationToken),
            cancellationToken);
    }

    public Task CompletePendingPullRecoveryAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(recovery);
        return RunSerializedAsync(
            () => CompletePendingPullRecoveryCoreAsync(recovery, cancellationToken),
            cancellationToken);
    }

    public Task RestoreProjectCheckpointAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(checkpoint);
        return RunSerializedAsync(
            () => RestoreProjectCheckpointCoreAsync(checkpoint, cancellationToken),
            cancellationToken);
    }

    public Task<CommitResult> CommitProjectTreeAsync(
        CheckedOutBranchTip expectedCurrent,
        string sourceCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        GitRevisionValidator.ValidateCommitId(sourceCommit, nameof(sourceCommit));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return RunSerializedAsync(
            () => CommitProjectTreeCoreAsync(
                expectedCurrent,
                sourceCommit,
                message,
                kind,
                cancellationToken),
            cancellationToken);
    }

    public Task<bool> RevisionContainsProjectFileAsync(
        string sha,
        string projectFile,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        return RunSerializedAsync(
            () => RevisionContainsProjectFileCoreAsync(sha, projectFile, cancellationToken),
            cancellationToken);
    }

    private async Task<bool> RevisionContainsProjectFileCoreAsync(
        string sha,
        string projectFile,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        string relativeProjectFile = GetRecoveryProjectFile(repository, projectFile);
        try
        {
            await runner.RunAsync(
                repository,
                ["cat-file", "-e", $"{sha}:{relativeProjectFile}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 128)
        {
            return false;
        }
    }

    public Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(target);
        return RunSerializedAsync(
            () => TryRollbackBranchTipCoreAsync(expectedCurrent, target, cancellationToken),
            cancellationToken);
    }

    public Task<bool> DeleteProjectCheckpointAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(checkpoint);
        return RunSerializedAsync(
            () => DeleteProjectCheckpointCoreAsync(checkpoint, cancellationToken),
            cancellationToken);
    }

    public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetStatusCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return RunSerializedAsync(
            () => GetHistoryCoreAsync(skip, take, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(
        string sha,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        return RunSerializedAsync(
            () => GetCommitFilesCoreAsync(sha, cancellationToken),
            cancellationToken);
    }

    public Task<string> GetDiffAsync(
        string sha,
        string? path,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        return RunSerializedAsync(
            () => GetDiffCoreAsync(sha, path, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetBranchesCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task CreateBranchAsync(
        string name,
        string startPoint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        GitRevisionValidator.ValidateCommitId(startPoint, nameof(startPoint));
        return RunSerializedAsync(
            () => CreateBranchCoreAsync(name, startPoint, cancellationToken),
            cancellationToken);
    }

    public Task SwitchBranchAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateSwitchBranchName(name);

        return RunSerializedAsync(
            () => SwitchBranchCoreAsync(name, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetRemotesCoreAsync(cancellationToken),
            cancellationToken);
    }

    public Task SetRemoteAsync(
        string url,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ValidateRemoteUrl(url);
        return RunSerializedAsync(
            () => SetRemoteCoreAsync(url, cancellationToken),
            cancellationToken);
    }

    public Task<RemoteOpResult> PushAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => PushCoreAsync(progress, cancellationToken),
            cancellationToken);
    }

    public Task<FastForwardPullResult> PullFastForwardAsync(
        CheckedOutBranchTip expectedCurrent,
        ProjectCheckpoint? checkpoint,
        string projectFile,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        return RunSerializedAsync(
            () => PullFastForwardCoreAsync(
                expectedCurrent,
                checkpoint,
                projectFile,
                cancellationToken),
            cancellationToken);
    }

    public Task<GitIdentity?> GetIdentityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
                return await GetIdentityCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task SetLocalIdentityAsync(
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Email);
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
                await SetLocalIdentityCoreAsync(
                        repository,
                        runner,
                        identity,
                        cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<bool> RemoveRecoverableLockAsync(
        RepositoryLockInfo expectedLock,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedLock);
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                RepositoryLockInfo? lockInfo = RecoverableLock;
                if (!ReferenceEquals(lockInfo, expectedLock))
                {
                    return false;
                }

                bool removed = runner.RemoveRecoverableRepositoryLock(repository, expectedLock);
                if (removed)
                {
                    RecoverableLock = null;
                }

                return removed;
            },
            cancellationToken);
    }

    public void Dispose()
    {
        Task retirement = RetireAsync(finalSnapshot: null);
        if (!retirement.IsCompletedSuccessfully)
        {
            _ = ObserveRetirementAsync(retirement);
        }
    }

    Task<TResult> IProjectVersionControlBackend.ExecuteExclusiveAsync<TResult>(
        Func<IProjectVersionControlTransaction, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        return ExecuteExclusiveCoreAsync(operation, cancellationToken);
    }

    public Task<CommitResult?> RetireAsync(ProjectVersionControlFinalSnapshot? finalSnapshot)
    {
        lock (_lifetimeSync)
        {
            if (_retirementTask is not null)
            {
                return _retirementTask;
            }

            if ((ServiceLifetimeState)_lifetimeState == ServiceLifetimeState.Retired)
            {
                return Task.FromResult<CommitResult?>(null);
            }

            _lifetimeState = (int)ServiceLifetimeState.Retiring;
            _retirementTask = RetireCoreAsync(finalSnapshot);
            return _retirementTask;
        }
    }

    private async Task<TResult> ExecuteExclusiveCoreAsync<TResult>(
        Func<IProjectVersionControlTransaction, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await operation(new Transaction(this)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<CommitResult?> RetireCoreAsync(
        ProjectVersionControlFinalSnapshot? finalSnapshot)
    {
        await Task.Yield();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (finalSnapshot is not null && Repository is not null)
            {
                return await CommitAllCoreAsync(
                        finalSnapshot.Message,
                        finalSnapshot.Kind,
                        CancellationToken.None,
                        presentMissingIdentityNotice: false)
                    .ConfigureAwait(false);
            }

            return null;
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            DisposeResources();
            Volatile.Write(ref _lifetimeState, (int)ServiceLifetimeState.Retired);
            _operationGate.Release();
        }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        RepositoryWatcher? watcher;
        lock (_lifetimeSync)
        {
            watcher = _watcher;
            _watcher = null;
            if (watcher is not null)
            {
                watcher.Changed -= OnRepositoryChanged;
            }
        }

        watcher?.Dispose();
        _installationLocator.Config.ConfigurationChanged -= OnVersionControlConfigChanged;
    }

    private async Task ObserveRetirementAsync(Task retirement)
    {
        try
        {
            await retirement.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retire the project version-control service.");
        }
    }

    internal static WorkspaceStatus ParseStatus(string output)
    {
        string? branch = null;
        int ahead = 0;
        int behind = 0;
        bool hasConflicts = false;
        var changes = new List<FileChange>();
        IReadOnlyList<string> records = GitCliRunner.SplitNullSeparated(output);

        for (int index = 0; index < records.Count; index++)
        {
            string record = records[index];
            if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                string head = record["# branch.head ".Length..];
                branch = head == "(detached)" ? null : head;
            }
            else if (record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                string[] values = record["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (string value in values)
                {
                    if (value.Length < 2)
                    {
                        continue;
                    }

                    if (value[0] == '+'
                        && int.TryParse(value.AsSpan(1), out int parsedAhead))
                    {
                        ahead = parsedAhead;
                    }
                    else if (value[0] == '-'
                             && int.TryParse(value.AsSpan(1), out int parsedBehind))
                    {
                        behind = parsedBehind;
                    }
                }
            }
            else if (record.StartsWith("1 ", StringComparison.Ordinal))
            {
                string statusCode = GetField(record, 1);
                string path = GetTailAfterSpaces(record, 8);
                changes.Add(new FileChange(path, MapStatus(statusCode)));
                hasConflicts |= statusCode.Contains('U');
            }
            else if (record.StartsWith("2 ", StringComparison.Ordinal))
            {
                string statusCode = GetField(record, 1);
                string renameOrCopy = GetField(record, 8);
                string path = GetTailAfterSpaces(record, 9);
                string? oldPath = ++index < records.Count ? records[index] : null;
                changes.Add(renameOrCopy.StartsWith('C')
                    ? new FileChange(path, FileChangeStatus.Added)
                    : new FileChange(path, FileChangeStatus.Renamed, oldPath));
                hasConflicts |= statusCode.Contains('U');
            }
            else if (record.StartsWith("u ", StringComparison.Ordinal))
            {
                string path = GetTailAfterSpaces(record, 10);
                changes.Add(new FileChange(path, FileChangeStatus.Modified));
                hasConflicts = true;
            }
            else if (record.StartsWith("? ", StringComparison.Ordinal))
            {
                changes.Add(new FileChange(record[2..], FileChangeStatus.Added));
            }
        }

        return new WorkspaceStatus(branch, ahead, behind, changes, hasConflicts);
    }

    internal static IReadOnlyList<CommitInfo> ParseHistory(string output)
    {
        string[] fields = output.Split('\0');
        var commits = new List<CommitInfo>(fields.Length / 6);
        int index = 0;
        while (index + 5 < fields.Length)
        {
            if (!DateTimeOffset.TryParse(
                    fields[index + 3],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTimeOffset authorDate))
            {
                break;
            }

            commits.Add(new CommitInfo(
                fields[index],
                fields[index + 1],
                fields[index + 4],
                fields[index + 2],
                authorDate,
                ParseSnapshotKind(fields[index + 5])));
            index += 6;
            while (index < fields.Length && fields[index].Length == 0)
            {
                index++;
            }
        }

        return commits;
    }

    internal static IReadOnlyList<FileChange> ParseCommitFiles(string output)
    {
        IReadOnlyList<string> fields = GitCliRunner.SplitNullSeparated(output);
        var changes = new List<FileChange>();
        for (int index = 0; index < fields.Count;)
        {
            string status = fields[index++].Trim();
            if (status.Length == 0 || index >= fields.Count)
            {
                break;
            }

            char statusCode = status[0];
            if (statusCode is 'R' or 'C')
            {
                if (index + 1 >= fields.Count)
                {
                    break;
                }

                string oldPath = fields[index++];
                string path = fields[index++];
                changes.Add(statusCode == 'C'
                    ? new FileChange(path, FileChangeStatus.Added)
                    : new FileChange(path, FileChangeStatus.Renamed, oldPath));
            }
            else
            {
                string path = fields[index++];
                changes.Add(new FileChange(path, MapNameStatus(statusCode)));
            }
        }

        return changes;
    }

    internal static IReadOnlyList<BranchInfo> ParseBranches(string output)
    {
        var branches = new List<BranchInfo>();
        foreach (string record in output
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\0');
            if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]))
            {
                continue;
            }

            string upstream = fields[2].Trim();
            branches.Add(new BranchInfo(
                fields[0],
                fields[1].Trim() == "*",
                string.IsNullOrEmpty(upstream) ? null : upstream));
        }

        return branches;
    }

    private static string GetField(string record, int fieldIndex)
    {
        string[] fields = record.Split(' ', fieldIndex + 2, StringSplitOptions.None);
        return fields.Length > fieldIndex ? fields[fieldIndex] : string.Empty;
    }

    private static string GetTailAfterSpaces(string record, int spaces)
    {
        int position = -1;
        for (int index = 0; index < spaces; index++)
        {
            position = record.IndexOf(' ', position + 1);
            if (position < 0)
            {
                return string.Empty;
            }
        }

        return record[(position + 1)..];
    }

    private static FileChangeStatus MapStatus(string statusCode)
    {
        if (statusCode.Contains('R'))
        {
            return FileChangeStatus.Renamed;
        }

        if (statusCode.Contains('C'))
        {
            return FileChangeStatus.Added;
        }

        if (statusCode.Contains('D'))
        {
            return FileChangeStatus.Deleted;
        }

        if (statusCode.Contains('A') || statusCode == "??")
        {
            return FileChangeStatus.Added;
        }

        return FileChangeStatus.Modified;
    }

    private async Task<CheckedOutBranchTip> GetCheckedOutBranchTipCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        return await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CheckedOutBranchTip> GetCheckedOutBranchTipCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string refName = await GetAttachedBranchRefCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult commit = await runner.RunAsync(
            repository,
            ["rev-parse", "--verify", $"{refName}^{{commit}}"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return new CheckedOutBranchTip(refName, commit.Stdout.Trim());
    }

    private static async Task<string> GetAttachedBranchRefCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult symbolicRef;
        try
        {
            symbolicRef = await runner.RunAsync(
                repository,
                ["symbolic-ref", "--quiet", "HEAD"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            throw new DetachedHeadNotSupportedException();
        }

        string refName = symbolicRef.Stdout.Trim();
        if (!refName.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            throw new DetachedHeadNotSupportedException();
        }

        return refName;
    }

    private async Task<ProjectCheckpoint> CreateProjectCheckpointCoreAsync(
        string message,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        ValidateProjectSnapshotLayout(repository.ProjectRoot);
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip baseHead = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!await IsProjectIndexCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new ProjectCheckpointStagedChangesException();
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            await RaiseMissingIdentityNoticeIfNeededAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new GitIdentityRequiredException();
        }

        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseHead.Commit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            SnapshotIndexCommandPlan indexPlan = await CreateSnapshotIndexCommandsAsync(
                    repository,
                    runner,
                    baseHead.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (IReadOnlyList<string> command in indexPlan.Commands)
            {
                await runner.RunAsync(
                        repository,
                        command,
                        indexOptions with
                        {
                            ExecutionKind = GitCommandExecutionKind.LocalWithLfs,
                            UseLiteralPathspecs = false,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            string treeId = tree.Stdout.Trim();
            await EnsureSnapshotTreeContainsNoGitlinksAsync(
                    repository,
                    runner,
                    treeId,
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            GitCommandResult commit = await runner.RunAsync(
                repository,
                [
                    "commit-tree",
                    treeId,
                    "-p",
                    baseHead.Commit,
                    "-m",
                    message.Trim(),
                    "-m",
                    "Beutl-Snapshot: safety",
                ],
                indexOptions,
                CancellationToken.None).ConfigureAwait(false);
            string checkpointCommit = commit.Stdout.Trim();
            string checkpointRef = GetCheckpointRefPrefix(repository)
                                   + Guid.NewGuid().ToString("N");
            var checkpoint = new ProjectCheckpoint(checkpointRef, checkpointCommit, baseHead);
            try
            {
                await runner.RunAsync(
                    repository,
                    [
                        "update-ref",
                        "--create-reflog",
                        "-m",
                        "beutl safety checkpoint",
                        checkpointRef,
                        checkpointCommit,
                        string.Empty,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None).ConfigureAwait(false);
                return checkpoint;
            }
            catch (Exception publicationException)
            {
                string? observedCommit;
                try
                {
                    observedCommit = await TryResolveCommitAsync(
                            repository,
                            runner,
                            checkpointRef,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception observationException)
                {
                    throw new AggregateException(
                        "The safety checkpoint ref publication failed and its durable result could not be observed.",
                        publicationException,
                        observationException);
                }

                if (string.Equals(
                        observedCommit,
                        checkpointCommit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return checkpoint;
                }

                if (observedCommit is null)
                {
                    throw;
                }

                throw new ProjectCheckpointChangedException(checkpointRef);
            }
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private async Task<PendingPullRecovery> PersistPendingPullRecoveryCoreAsync(
        ProjectCheckpoint checkpoint,
        CheckedOutBranchTip targetTip,
        string projectFile,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);
        ValidateAttachedBranchTip(targetTip, nameof(targetTip));
        if (!string.Equals(
                targetTip.RefName,
                checkpoint.BaseTip.RefName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The recovery target must identify the checkpoint's local branch.",
                nameof(targetTip));
        }

        string? resolvedTarget = await TryResolveCommitAsync(
                repository,
                runner,
                targetTip.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                resolvedTarget,
                targetTip.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The recovery target must resolve to an existing commit.",
                nameof(targetTip));
        }

        string relativeProjectFile = GetRecoveryProjectFile(repository, projectFile);
        string absoluteProjectFile = GetLexicalRecoveryProjectFile(
            repository,
            relativeProjectFile);
        ValidateRecoveryProjectFilePhysicalContainment(repository, absoluteProjectFile);
        string id = Guid.NewGuid().ToString("N");
        string descriptorRef = GetPendingRecoveryRefPrefix(repository) + id;
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        var data = new PendingPullRecoveryData(
            PendingPullRecoveryFormatVersion,
            id,
            checkpoint.RefName,
            checkpoint.Commit,
            checkpoint.BaseTip.RefName,
            checkpoint.BaseTip.Commit,
            targetTip.Commit,
            relativeProjectFile,
            createdAt);
        string json = JsonSerializer.Serialize(data, s_recoveryJsonOptions);
        GitCommandResult descriptorObjectResult = await runner.RunAsync(
            repository,
            ["hash-object", "-w", "--stdin"],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                StandardInput: json),
            cancellationToken).ConfigureAwait(false);
        string descriptorObject = descriptorObjectResult.Stdout.Trim();
        GitRevisionValidator.ValidateCommitId(descriptorObject, nameof(descriptorObject));

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await runner.RunAsync(
                repository,
                [
                    "update-ref",
                    "--create-reflog",
                    "-m",
                    "beutl pending pull recovery",
                    descriptorRef,
                    descriptorObject,
                    string.Empty,
                ],
                GitCommandOptions.Local,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception publicationException)
        {
            string? observedObject;
            try
            {
                observedObject = await TryResolveObjectAsync(
                        repository,
                        runner,
                        descriptorRef,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception observationException)
            {
                throw new AggregateException(
                    "The pending pull recovery publication failed and its durable result could not be observed.",
                    publicationException,
                    observationException);
            }

            if (!string.Equals(
                    observedObject,
                    descriptorObject,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (observedObject is null)
                {
                    throw;
                }

                throw new PendingPullRecoveryChangedException(descriptorRef);
            }
        }

        return new PendingPullRecovery(
            id,
            descriptorRef,
            descriptorObject,
            checkpoint,
            targetTip,
            absoluteProjectFile,
            createdAt);
    }

    private async Task<IReadOnlyList<PendingPullRecovery>> GetPendingPullRecoveriesCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult refs = await runner.RunAsync(
            repository,
            [
                "for-each-ref",
                "--sort=refname",
                "--format=%(refname)%00%(objectname)",
                GetPendingRecoveryRefPrefix(repository),
            ],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                MaxStdoutBytes: MaxPendingRecoveryListBytes),
            cancellationToken).ConfigureAwait(false);
        if (refs.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The pending pull recovery list exceeded the safe output limit.");
        }

        var result = new List<PendingPullRecovery>();
        foreach (string rawLine in refs.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string line = rawLine.TrimEnd('\r');
            string[] fields = line.Split('\0');
            if (fields.Length != 2)
            {
                _logger.LogWarning(
                    "Ignored a malformed pending pull recovery ref record in {RepositoryRoot}.",
                    repository.RepoRoot);
                continue;
            }

            try
            {
                PendingPullRecovery? recovery = await ReadPendingPullRecoveryAsync(
                        repository,
                        runner,
                        fields[0],
                        fields[1],
                        cancellationToken)
                    .ConfigureAwait(false);
                if (recovery is not null)
                {
                    result.Add(recovery);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ignored invalid pending pull recovery descriptor {RecoveryRef}.",
                    fields[0]);
            }
        }

        return result
            .OrderBy(static recovery => recovery.CreatedAt)
            .ThenBy(static recovery => recovery.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<PendingPullRecovery?> ReadPendingPullRecoveryAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string descriptorRef,
        string descriptorObject,
        CancellationToken cancellationToken)
    {
        string prefix = GetPendingRecoveryRefPrefix(repository);
        if (!descriptorRef.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string id = descriptorRef[prefix.Length..];
        if (!Guid.TryParseExact(id, "N", out _)
            || id.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        GitRevisionValidator.ValidateCommitId(descriptorObject, nameof(descriptorObject));
        GitCommandResult descriptor = await runner.RunAsync(
            repository,
            ["cat-file", "blob", descriptorObject],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                MaxStdoutBytes: MaxPendingRecoveryDescriptorBytes),
            cancellationToken).ConfigureAwait(false);
        if (descriptor.StdoutTruncated)
        {
            return null;
        }

        PendingPullRecoveryData? data = JsonSerializer.Deserialize<PendingPullRecoveryData>(
            descriptor.Stdout,
            s_recoveryJsonOptions);
        if (data is null
            || data.Version != PendingPullRecoveryFormatVersion
            || !string.Equals(data.Id, id, StringComparison.Ordinal))
        {
            return null;
        }

        var checkpoint = new ProjectCheckpoint(
            data.CheckpointRef,
            data.CheckpointCommit,
            new CheckedOutBranchTip(data.BranchRef, data.BaseCommit));
        var targetTip = new CheckedOutBranchTip(data.BranchRef, data.TargetCommit);
        ValidateCheckpointRef(repository, checkpoint);
        ValidateAttachedBranchTip(targetTip, nameof(data.TargetCommit));
        if (!string.Equals(
                checkpoint.BaseTip.RefName,
                targetTip.RefName,
                StringComparison.Ordinal)
            || data.CreatedAt == default)
        {
            return null;
        }

        string projectFile = GetLexicalRecoveryProjectFile(repository, data.ProjectFile);
        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);

        return new PendingPullRecovery(
            id,
            descriptorRef,
            descriptorObject,
            checkpoint,
            targetTip,
            projectFile,
            data.CreatedAt);
    }

    private async Task<PendingPullRecoveryOutcome> RecoverPendingPullRecoveryCoreAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await ValidatePendingPullRecoveryAsync(
                repository,
                runner,
                recovery,
                cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip actualTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (EqualsBranchTip(actualTip, recovery.TargetTip))
        {
            WorktreeStateFingerprint actualState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    recovery.TargetTip.Commit,
                    ".",
                    cancellationToken)
                .ConfigureAwait(false);
            string targetTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    recovery.TargetTip.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    actualState.Tree,
                    targetTree,
                    StringComparison.OrdinalIgnoreCase)
                || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        new InvalidOperationException(
                            "The pulled branch tip is present, but its worktree state cannot be verified."))
                    .ConfigureAwait(false);
            }

            TreeTransitionResult rollback;
            try
            {
                rollback = await ApplyTreeTransitionAsync(
                        repository,
                        runner,
                        recovery.TargetTip,
                        recovery.Checkpoint.BaseTip,
                        recovery.TargetTip.Commit,
                        recovery.Checkpoint.BaseTip.Commit,
                        "beutl roll back pending pull target",
                        indexPlan: null,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        ex)
                    .ConfigureAwait(false);
            }

            if (rollback.Outcome != TreeTransitionOutcome.AppliedTarget)
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        rollback.Error
                        ?? new InvalidOperationException(
                            "The pulled branch could not be rolled back safely."))
                    .ConfigureAwait(false);
            }

            try
            {
                await RestoreProjectCheckpointCoreAsync(
                        recovery.Checkpoint,
                        CancellationToken.None,
                        validatePreparedTarget: () =>
                            ValidateRecoveryProjectFilePhysicalContainment(
                                repository,
                                recovery.ProjectFile))
                    .ConfigureAwait(false);
                ValidateRecoveryProjectFilePhysicalContainment(
                    repository,
                    recovery.ProjectFile);
                return PendingPullRecoveryOutcome.RestoredOriginal;
            }
            catch (Exception ex)
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        ex)
                    .ConfigureAwait(false);
            }
        }

        if (!EqualsBranchTip(actualTip, recovery.Checkpoint.BaseTip))
        {
            string recoveryBranchName;
            try
            {
                recoveryBranchName = await PreserveCheckpointOnRecoveryBranchAsync(
                        repository,
                        runner,
                        recovery,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw await CreateCheckpointPreservationExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        ex)
                    .ConfigureAwait(false);
            }

            try
            {
                if (!await TryReapplyCheckpointToExternallyOwnedTipAsync(
                        repository,
                        runner,
                        recovery,
                        actualTip,
                        CancellationToken.None)
                    .ConfigureAwait(false))
                {
                    throw new PendingPullRecoveryPreservedException(recoveryBranchName);
                }

                ValidateRecoveryProjectFilePhysicalContainment(
                    repository,
                    recovery.ProjectFile);
                return PendingPullRecoveryOutcome.ReappliedCheckpoint;
            }
            catch (PendingPullRecoveryPreservedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PendingPullRecoveryPreservedException(recoveryBranchName, ex);
            }
        }

        try
        {
            await RestoreProjectCheckpointCoreAsync(
                    recovery.Checkpoint,
                    CancellationToken.None,
                    validatePreparedTarget: () =>
                        ValidateRecoveryProjectFilePhysicalContainment(
                            repository,
                            recovery.ProjectFile))
                .ConfigureAwait(false);
            CheckedOutBranchTip recoveredTip = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(recoveredTip, recovery.Checkpoint.BaseTip))
            {
                throw new InvalidOperationException(
                    "The repository branch changed while the pending pull recovery was restored.");
            }

            ValidateRecoveryProjectFilePhysicalContainment(
                repository,
                recovery.ProjectFile);
            return PendingPullRecoveryOutcome.RestoredOriginal;
        }
        catch (Exception ex)
        {
            throw await CreatePreservedRecoveryExceptionAsync(
                    repository,
                    runner,
                    recovery,
                    ex)
                .ConfigureAwait(false);
        }
    }

    private static async Task<Exception>
        CreatePreservedRecoveryExceptionAsync(
            RepositoryInfo repository,
            IGitCliRunner runner,
            PendingPullRecovery recovery,
            Exception failure)
    {
        try
        {
            string recoveryBranchName = await PreserveCheckpointOnRecoveryBranchAsync(
                    repository,
                    runner,
                    recovery,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return new PendingPullRecoveryPreservedException(
                recoveryBranchName,
                failure);
        }
        catch (Exception preservationFailure)
        {
            return await CreateCheckpointPreservationExceptionAsync(
                    repository,
                    runner,
                    recovery,
                    new AggregateException(
                    "The pending pull recovery failed and its durable recovery branch could not be published.",
                    failure,
                    preservationFailure))
                .ConfigureAwait(false);
        }
    }

    private static async Task<Exception> CreateCheckpointPreservationExceptionAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        Exception failure)
    {
        try
        {
            string? checkpointCommit = await TryResolveCommitAsync(
                    repository,
                    runner,
                    recovery.Checkpoint.RefName,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (string.Equals(
                    checkpointCommit,
                    recovery.Checkpoint.Commit,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new PendingPullRecoveryPreservedException(
                    recovery.Checkpoint.RefName,
                    failure);
            }

            return new AggregateException(
                "The pending pull recovery failed and its checkpoint reference no longer identifies the expected commit.",
                failure);
        }
        catch (Exception verificationFailure)
        {
            return new AggregateException(
                "The pending pull recovery failed and its checkpoint reference could not be verified.",
                failure,
                verificationFailure);
        }
    }

    private static async Task<string> PreserveCheckpointOnRecoveryBranchAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        string branchName = recovery.RecoveryBranchName;
        string branchRef = $"refs/heads/{branchName}";
        string? existing = await TryResolveCommitAsync(
                repository,
                runner,
                branchRef,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(
                existing,
                recovery.Checkpoint.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            return branchName;
        }

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"The recovery branch '{branchName}' already identifies another commit.");
        }

        try
        {
            await runner.RunAsync(
                repository,
                [
                    "update-ref",
                    "--create-reflog",
                    "-m",
                    "beutl preserve pending pull checkpoint",
                    branchRef,
                    recovery.Checkpoint.Commit,
                    string.Empty,
                ],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception publicationException)
        {
            existing = await TryResolveCommitAsync(
                    repository,
                    runner,
                    branchRef,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!string.Equals(
                    existing,
                    recovery.Checkpoint.Commit,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new AggregateException(
                    $"The recovery branch '{branchName}' could not be published safely.",
                    publicationException);
            }
        }

        return branchName;
    }

    private async Task<bool> TryReapplyCheckpointToExternallyOwnedTipAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        CheckedOutBranchTip actualTip,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                actualTip.RefName,
                recovery.Checkpoint.BaseTip.RefName,
                StringComparison.Ordinal))
        {
            return false;
        }

        // Validate before any temporary commit or checkout can replace a symlinked project path.
        ValidateRecoveryProjectFilePhysicalContainment(
            repository,
            recovery.ProjectFile);

        string desiredTree = await BuildProjectTreeAsync(
                repository,
                runner,
                actualTip.Commit,
                recovery.Checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        WorktreeStateFingerprint actualState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                actualTip.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string actualTree = await ResolveTreeAsync(
                repository,
                runner,
                actualTip.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        bool indexAtActual = await IsIndexAtCommitAsync(
                repository,
                runner,
                actualTip.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(actualState.Tree, desiredTree, StringComparison.OrdinalIgnoreCase))
        {
            bool indexAtBase = indexAtActual || await IsIndexAtCommitAsync(
                    repository,
                    runner,
                    recovery.Checkpoint.BaseTip.Commit,
                    repository.Pathspec,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!indexAtBase)
            {
                return false;
            }

            CheckedOutBranchTip beforeReset = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(beforeReset, actualTip))
            {
                return false;
            }

            string originalIndexCommit = indexAtActual
                ? actualTip.Commit
                : recovery.Checkpoint.BaseTip.Commit;
            try
            {
                if (!indexAtActual)
                {
                    await ResetIndexAsync(
                            repository,
                            runner,
                            actualTip.Commit,
                            repository.Pathspec)
                        .ConfigureAwait(false);
                }

                CheckedOutBranchTip verifiedTip = await GetCheckedOutBranchTipCoreAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                WorktreeStateFingerprint verifiedState = await CaptureWorktreeStateAsync(
                        repository,
                        runner,
                        actualTip.Commit,
                        repository.Pathspec,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!EqualsBranchTip(verifiedTip, actualTip)
                    || !string.Equals(
                        verifiedState.Tree,
                        desiredTree,
                        StringComparison.OrdinalIgnoreCase)
                    || !await IsIndexAtCommitAsync(
                            repository,
                            runner,
                            actualTip.Commit,
                            repository.Pathspec,
                            CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    throw new ProjectCheckpointStateChangedException();
                }

                return true;
            }
            catch (Exception ex)
            {
                Exception failure = ex;
                if (!indexAtActual)
                {
                    try
                    {
                        await ResetIndexAsync(
                                repository,
                                runner,
                            originalIndexCommit,
                            repository.Pathspec)
                            .ConfigureAwait(false);
                    }
                    catch (Exception restoreException)
                    {
                        failure = new AggregateException(
                            "The checkpoint index reapply failed and the prior index could not be restored.",
                            ex,
                            restoreException);
                    }
                }

                throw failure;
            }
        }

        if (!string.Equals(actualState.Tree, actualTree, StringComparison.OrdinalIgnoreCase)
            || !indexAtActual)
        {
            return false;
        }

        string targetCommit = await CreateTreeCommitAsync(
                repository,
                runner,
                desiredTree,
                actualTip.Commit,
                "beutl temporary pending pull recovery",
                cancellationToken)
            .ConfigureAwait(false);

        TreeTransitionResult transition = await ApplyTreeTransitionAsync(
                repository,
                runner,
                actualTip,
                actualTip,
                actualTip.Commit,
                targetCommit,
                "beutl reapply pending pull checkpoint",
                new TreeTransitionIndexPlan(
                    FinalCommit: actualTip.Commit,
                    RestoreCommit: actualTip.Commit,
                    Pathspec: repository.Pathspec),
                cancellationToken,
                validatePreparedTarget: () =>
                    ValidateRecoveryProjectFilePhysicalContainment(
                        repository,
                        recovery.ProjectFile))
            .ConfigureAwait(false);
        return transition.Outcome switch
        {
            TreeTransitionOutcome.AppliedTarget => true,
            TreeTransitionOutcome.OwnershipLost => false,
            _ => throw new InvalidOperationException(
                "The pending pull checkpoint could not be reapplied safely.",
                transition.Error),
        };
    }

    private static async Task<string> CreateTreeCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        string parentCommit,
        string message,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                ["commit-tree", tree, "-p", parentCommit, "-m", message],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    EnvironmentOverrides: new Dictionary<string, string?>
                    {
                        ["GIT_AUTHOR_NAME"] = "Beutl Recovery",
                        ["GIT_AUTHOR_EMAIL"] = "beutl-recovery@localhost",
                        ["GIT_COMMITTER_NAME"] = "Beutl Recovery",
                        ["GIT_COMMITTER_EMAIL"] = "beutl-recovery@localhost",
                    }),
                cancellationToken)
            .ConfigureAwait(false);
        string commit = result.Stdout.Trim();
        if (commit.Length == 0)
        {
            throw new InvalidOperationException("Git did not return the temporary recovery commit.");
        }

        await runner.RunAsync(
                repository,
                ["cat-file", "-e", commit + "^{commit}"],
                GitCommandOptions.Local,
                cancellationToken)
            .ConfigureAwait(false);

        return commit;
    }

    private static async Task<string> CreateReservedPathCleanupCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        string parentCommit,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                [
                    "commit-tree",
                    tree,
                    "-p",
                    parentCommit,
                    "-m",
                    "beutl: stop tracking reserved project state",
                    "-m",
                    "Beutl-Snapshot: init",
                ],
                GitCommandOptions.Local,
                cancellationToken)
            .ConfigureAwait(false);
        string commit = result.Stdout.Trim();
        if (commit.Length == 0)
        {
            throw new InvalidOperationException(
                "Git did not return the reserved-path cleanup commit.");
        }

        await runner.RunAsync(
                repository,
                ["cat-file", "-e", commit + "^{commit}"],
                GitCommandOptions.Local,
                cancellationToken)
            .ConfigureAwait(false);

        return commit;
    }

    private async Task CompletePendingPullRecoveryCoreAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await ValidatePendingPullRecoveryAsync(
                repository,
                runner,
                recovery,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        string commands = string.Join(
            '\n',
            "start",
            $"delete {recovery.DescriptorRef} {recovery.DescriptorObject}",
            $"delete {recovery.Checkpoint.RefName} {recovery.Checkpoint.Commit}",
            "prepare",
            "commit",
            string.Empty);
        try
        {
            await runner.RunAsync(
                repository,
                ["update-ref", "--stdin"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    StandardInput: commands),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            string? remainingDescriptor = await TryResolveObjectAsync(
                    repository,
                    runner,
                    recovery.DescriptorRef,
                    CancellationToken.None)
                .ConfigureAwait(false);
            string? remainingCheckpoint = await TryResolveCommitAsync(
                    repository,
                    runner,
                    recovery.Checkpoint.RefName,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (remainingDescriptor is null && remainingCheckpoint is null)
            {
                return;
            }

            throw new PendingPullRecoveryChangedException(recovery.DescriptorRef, ex);
        }
    }

    private static async Task ValidatePendingPullRecoveryAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        string expectedRef = GetPendingRecoveryRefPrefix(repository) + recovery.Id;
        if (!Guid.TryParseExact(recovery.Id, "N", out _)
            || !string.Equals(recovery.DescriptorRef, expectedRef, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The pending pull recovery does not belong to this project.",
                nameof(recovery));
        }

        GitRevisionValidator.ValidateCommitId(
            recovery.DescriptorObject,
            nameof(recovery));
        ValidateCheckpointRef(repository, recovery.Checkpoint);
        ValidateAttachedBranchTip(recovery.TargetTip, nameof(recovery));
        if (!string.Equals(
                recovery.Checkpoint.BaseTip.RefName,
                recovery.TargetTip.RefName,
                StringComparison.Ordinal)
            || recovery.CreatedAt == default)
        {
            throw new ArgumentException(
                "The pending pull recovery descriptor is inconsistent.",
                nameof(recovery));
        }

        _ = ValidateStoredRecoveryProjectFile(repository, recovery.ProjectFile);
        string? currentObject = await TryResolveObjectAsync(
                repository,
                runner,
                recovery.DescriptorRef,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                currentObject,
                recovery.DescriptorObject,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PendingPullRecoveryChangedException(recovery.DescriptorRef);
        }

        await ValidateCheckpointAsync(repository, runner, recovery.Checkpoint, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string GetRecoveryProjectFile(
        RepositoryInfo repository,
        string projectFile)
    {
        string projectRoot = RepositoryPathComparer.ResolveCanonicalPath(repository.ProjectRoot);
        string fullPath = RepositoryPathComparer.ResolveCanonicalPath(projectFile);
        string canonicalRelativePath = Path.GetRelativePath(projectRoot, fullPath);
        ValidateRecoveryProjectFileContainment(canonicalRelativePath);

        return GetRecoveryProjectFileLexically(
            repository,
            projectFile,
            canonicalRelativePath);
    }

    private static string GetRecoveryProjectFileLexically(
        RepositoryInfo repository,
        string projectFile,
        string? canonicalRelativePath = null)
    {

        string lexicalProjectFile = Path.GetFullPath(projectFile);
        string? lexicalRoot = Path.GetDirectoryName(lexicalProjectFile);
        while (lexicalRoot is not null)
        {
            if (RepositoryPathComparer.AreEquivalent(lexicalRoot, repository.ProjectRoot))
            {
                string lexicalRelativePath = Path.GetRelativePath(
                    lexicalRoot,
                    lexicalProjectFile);
                ValidateRecoveryProjectFile(lexicalRelativePath);
                return NormalizeGitPath(lexicalRelativePath);
            }

            lexicalRoot = Path.GetDirectoryName(lexicalRoot);
        }

        canonicalRelativePath ??= Path.GetRelativePath(
            Path.GetFullPath(repository.ProjectRoot),
            lexicalProjectFile);
        ValidateRecoveryProjectFile(canonicalRelativePath);
        return NormalizeGitPath(canonicalRelativePath);
    }

    private static string GetLexicalRecoveryProjectFile(
        RepositoryInfo repository,
        string relativeProjectFile)
    {
        ValidateRecoveryProjectFile(relativeProjectFile);
        return Path.GetFullPath(Path.Combine(
            repository.ProjectRoot,
            relativeProjectFile.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ValidateStoredRecoveryProjectFile(
        RepositoryInfo repository,
        string projectFile)
    {
        string relativeProjectFile = Path.GetRelativePath(
            Path.GetFullPath(repository.ProjectRoot),
            Path.GetFullPath(projectFile));
        ValidateRecoveryProjectFile(relativeProjectFile);
        return NormalizeGitPath(relativeProjectFile);
    }

    private static void ValidateRecoveryProjectFilePhysicalContainment(
        RepositoryInfo repository,
        string projectFile)
    {
        if (!RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, projectFile))
        {
            throw new ArgumentException(
                $"The pending pull recovery project file '{projectFile}' must remain inside the project root.",
                nameof(projectFile));
        }
    }

    private static void ValidateRecoveryProjectFile(string relativePath)
    {
        ValidateRecoveryProjectFileContainment(relativePath);
        if (!string.Equals(
                Path.GetExtension(relativePath),
                ".bep",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The pending pull recovery project file '{relativePath}' must use the .bep extension.",
                nameof(relativePath));
        }
    }

    private static void ValidateRecoveryProjectFileContainment(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || relativePath
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(static component => component is "." or ".."))
        {
            throw new ArgumentException(
                $"The pending pull recovery project file '{relativePath}' must remain inside the project root.",
                nameof(relativePath));
        }
    }

    private async Task RestoreProjectCheckpointCoreAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken,
        Action? validatePreparedTarget = null)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip currentHead = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentHead, checkpoint.BaseTip))
        {
            throw new InvalidOperationException(
                "The project checkpoint can only be restored directly at its original head.");
        }

        WorktreeStateFingerprint currentState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string checkpointTree = await ResolveTreeAsync(
                repository,
                runner,
                checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(currentState.Tree, checkpointTree, StringComparison.OrdinalIgnoreCase)
            && await IsProjectIndexCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            validatePreparedTarget?.Invoke();
            return;
        }

        if (!await IsProjectCleanAsync(repository, runner, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The project must be clean before restoring a project checkpoint.");
        }

        string baseTree = await ResolveTreeAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(currentState.Tree, baseTree, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointStateChangedException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                repository.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(ownershipTip, checkpoint.BaseTip)
            || ownershipState != currentState)
        {
            throw new InvalidOperationException(
                "The project changed before its checkpoint could be restored.");
        }

        TreeTransitionResult transitionResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            checkpoint.BaseTip,
            checkpoint.BaseTip,
            checkpoint.BaseTip.Commit,
            checkpoint.Commit,
            "beutl restore project checkpoint",
            new TreeTransitionIndexPlan(
                FinalCommit: checkpoint.BaseTip.Commit,
                RestoreCommit: checkpoint.BaseTip.Commit,
                Pathspec: repository.Pathspec),
            CancellationToken.None,
            validatePreparedTarget).ConfigureAwait(false);
        EnsureTreeTransitionApplied(
            transitionResult,
            "The project checkpoint could not be restored safely.");
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task<CommitResult> CommitProjectTreeCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        string sourceCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(sourceCommit, nameof(sourceCommit));
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed before the project tree transition started.");
        }

        string? resolvedSource = await TryResolveCommitAsync(
                repository,
                runner,
                sourceCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (resolvedSource is null)
        {
            throw new ArgumentException(
                "The project tree source must resolve to a commit.",
                nameof(sourceCommit));
        }

        if (!await IsProjectCleanAsync(repository, runner, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The project must be clean before committing a project tree transition.");
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            if (kind != SnapshotKind.Manual)
            {
                await RaiseMissingIdentityNoticeIfNeededAsync(
                        repository,
                        runner,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new CommitResult.SkippedNoIdentity();
            }

            throw new GitIdentityRequiredException();
        }

        WorktreeStateFingerprint expectedState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(expectedState.Tree, expectedTree, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The project index or worktree changed before the project tree transition started.");
        }

        string desiredTree = await BuildProjectTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                resolvedSource,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(desiredTree, expectedTree, StringComparison.OrdinalIgnoreCase))
        {
            return new CommitResult.NoChanges();
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult commit = await runner.RunAsync(
            repository,
            [
                "commit-tree",
                desiredTree,
                "-p",
                expectedCurrent.Commit,
                "-m",
                message.Trim(),
                "-m",
                $"Beutl-Snapshot: {kind.ToString().ToLowerInvariant()}",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var committedTip = new CheckedOutBranchTip(
            expectedCurrent.RefName,
            commit.Stdout.Trim());

        cancellationToken.ThrowIfCancellationRequested();
        CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                repository.Pathspec,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(ownershipTip, expectedCurrent)
            || ownershipState != expectedState
            || !await IsProjectCleanAsync(repository, runner, CancellationToken.None)
                .ConfigureAwait(false))
        {
            throw new ProjectCheckpointStateChangedException();
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        TreeTransitionResult applyResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            committedTip,
            expectedCurrent.Commit,
            committedTip.Commit,
            $"commit: {message.Trim()}",
            new TreeTransitionIndexPlan(Pathspec: repository.Pathspec),
            CancellationToken.None).ConfigureAwait(false);
        EnsureTreeTransitionApplied(
            applyResult,
            "The project tree transition could not be applied safely.");
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new CommitResult.Committed(new CommitRevision.Known(committedTip.Commit));
    }

    private async Task<BranchTipRollbackResult> TryRollbackBranchTipCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        ValidateBranchTipForRollback(expectedCurrent, target);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        CheckedOutBranchTip? actualHead = await TryGetCheckedOutBranchTipAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (actualHead is null
            || !string.Equals(actualHead.RefName, expectedCurrent.RefName, StringComparison.Ordinal)
            || !string.Equals(actualHead.Commit, expectedCurrent.Commit, StringComparison.OrdinalIgnoreCase))
        {
            return new BranchTipRollbackResult.RefChanged(actualHead?.Commit);
        }

        if (!await IsAncestorAsync(
                repository,
                runner,
                target.Commit,
                expectedCurrent.Commit,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException(
                "The rollback target must be an ancestor of the expected current head.",
                nameof(target));
        }

        if (!await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        WorktreeStateFingerprint expectedWorktree = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                ".",
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(expectedWorktree.Tree, expectedTree, StringComparison.OrdinalIgnoreCase)
            || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        cancellationToken.ThrowIfCancellationRequested();
        TreeTransitionResult rollbackResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            target,
            expectedCurrent.Commit,
            target.Commit,
            "beutl rollback fast-forward pull",
            indexPlan: null,
            CancellationToken.None).ConfigureAwait(false);
        if (rollbackResult.Outcome == TreeTransitionOutcome.OwnershipLost)
        {
            if (rollbackResult.Error is VersionControlConflictedException)
            {
                return new BranchTipRollbackResult.UnsafeRepositoryState();
            }

            return new BranchTipRollbackResult.RefChanged(
                rollbackResult.ActualTip?.Commit);
        }

        if (rollbackResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (rollbackResult.Outcome == TreeTransitionOutcome.RecoveryFailed
                && rollbackResult.Error is not null)
            {
                throw rollbackResult.Error;
            }

            return new BranchTipRollbackResult.UnsafeRepositoryState();
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new BranchTipRollbackResult.RolledBack();
    }

    private async Task<bool> DeleteProjectCheckpointCoreAsync(
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        ValidateCheckpointRef(repository, checkpoint);
        string? currentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                checkpoint.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        if (currentCommit is null)
        {
            return false;
        }

        if (!string.Equals(currentCommit, checkpoint.Commit, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointChangedException(checkpoint.RefName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await runner.RunAsync(
            repository,
            ["update-ref", "-d", checkpoint.RefName, checkpoint.Commit],
            GitCommandOptions.Local,
            CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private static async Task ValidateCheckpointAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        ProjectCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ValidateCheckpointRef(repository, checkpoint);
        string? currentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                checkpoint.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        string? parentCommit = await TryResolveCommitAsync(
                repository,
                runner,
                $"{checkpoint.Commit}^1",
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(currentCommit, checkpoint.Commit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                parentCommit,
                checkpoint.BaseTip.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectCheckpointChangedException(checkpoint.RefName);
        }
    }

    private static void ValidateCheckpointRef(
        RepositoryInfo repository,
        ProjectCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.RefName);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Commit);
        ArgumentNullException.ThrowIfNull(checkpoint.BaseTip);
        string prefix = GetCheckpointRefPrefix(repository);
        if (!checkpoint.RefName.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(checkpoint.RefName[prefix.Length..], "N", out _))
        {
            throw new ArgumentException(
                "The checkpoint does not belong to this project.",
                nameof(checkpoint));
        }

        GitRevisionValidator.ValidateCommitId(checkpoint.Commit, nameof(checkpoint));
        ValidateAttachedBranchTip(checkpoint.BaseTip, nameof(checkpoint));
    }

    private static void ValidateBranchTipForRollback(
        CheckedOutBranchTip expectedCurrent,
        CheckedOutBranchTip target)
    {
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        ValidateAttachedBranchTip(target, nameof(target));
        if (!string.Equals(expectedCurrent.RefName, target.RefName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The rollback heads must identify the same local branch.",
                nameof(target));
        }
    }

    private static void ValidateAttachedBranchTip(CheckedOutBranchTip tip, string paramName)
    {
        ArgumentNullException.ThrowIfNull(tip, paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tip.RefName, paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tip.Commit, paramName);
        if (!IsValidLocalBranchRef(tip.RefName))
        {
            throw new ArgumentException("An attached local branch tip is required.", paramName);
        }

        GitRevisionValidator.ValidateCommitId(tip.Commit, paramName);
    }

    private static bool IsValidLocalBranchRef(string refName)
    {
        const string Prefix = "refs/heads/";
        if (!refName.StartsWith(Prefix, StringComparison.Ordinal)
            || refName.Length == Prefix.Length
            || refName.EndsWith("/", StringComparison.Ordinal)
            || refName.EndsWith(".", StringComparison.Ordinal)
            || refName.Contains("//", StringComparison.Ordinal)
            || refName.Contains("..", StringComparison.Ordinal)
            || refName.Contains("@{", StringComparison.Ordinal)
            || refName.Any(static character => character <= ' '
                || character == '\u007f'
                || character is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            return false;
        }

        foreach (string component in refName.Split('/'))
        {
            if (component.Length == 0
                || component.StartsWith(".", StringComparison.Ordinal)
                || component.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetBranchShortName(string refName)
    {
        const string Prefix = "refs/heads/";
        if (!refName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An attached local branch ref is required.",
                nameof(refName));
        }

        return refName[Prefix.Length..];
    }

    private static async Task<CheckedOutBranchTip?> TryGetCheckedOutBranchTipAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken).ConfigureAwait(false);
        }
        catch (DetachedHeadNotSupportedException)
        {
            return null;
        }
    }

    private static async Task<string?> TryResolveCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string revision,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", "--quiet", $"{revision}^{{commit}}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string commit = result.Stdout.Trim();
            return string.IsNullOrEmpty(commit) ? null : commit;
        }
        catch (GitOperationException ex) when (ex.ExitCode is 1 or 128
                                                && !ex.IsRepositoryLockFailure)
        {
            return null;
        }
    }

    private static async Task<string?> TryResolveCommitWithRetryAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string revision)
    {
        Exception? observationFailure = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string? commit = await TryResolveCommitAsync(
                        repository,
                        runner,
                        revision,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (commit is not null)
                {
                    return commit;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                observationFailure = ex;
            }
        }

        if (observationFailure is not null)
        {
            throw observationFailure;
        }

        return null;
    }

    private static async Task<string?> TryResolveObjectAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string revision,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", "--quiet", revision],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string objectId = result.Stdout.Trim();
            return string.IsNullOrEmpty(objectId) ? null : objectId;
        }
        catch (GitOperationException ex) when (ex.ExitCode is 1 or 128)
        {
            return null;
        }
    }

    private static async Task<string> ResolveTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string revision,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            ["rev-parse", "--verify", $"{revision}^{{tree}}"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return result.Stdout.Trim();
    }

    private async Task<SnapshotTreeBuildResult> BuildSnapshotTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string? baseCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.LocalWithLfs,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            })
        {
            UseLiteralPathspecs = false,
        };

        try
        {
            await runner.RunAsync(
                    repository,
                    baseCommit is null
                        ? ["read-tree", "--empty"]
                        : ["read-tree", baseCommit],
                    indexOptions with { ExecutionKind = GitCommandExecutionKind.Local },
                    cancellationToken)
                .ConfigureAwait(false);
            SnapshotIndexCommandPlan indexPlan = await CreateSnapshotIndexCommandsAsync(
                    repository,
                    runner,
                    baseCommit,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (IReadOnlyList<string> command in indexPlan.Commands)
            {
                await runner.RunAsync(
                        repository,
                        command,
                        indexOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            GitCommandResult tree = await runner.RunAsync(
                    repository,
                    ["write-tree"],
                    indexOptions with { ExecutionKind = GitCommandExecutionKind.Local },
                    cancellationToken)
                .ConfigureAwait(false);
            string treeId = tree.Stdout.Trim();
            GitRevisionValidator.ValidateCommitId(treeId, nameof(treeId));
            await EnsureSnapshotTreeContainsNoGitlinksAsync(
                    repository,
                    runner,
                    treeId,
                    cancellationToken)
                .ConfigureAwait(false);
            return new SnapshotTreeBuildResult(
                treeId,
                indexPlan.TemporaryPathspecsToReconcile);
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private async Task<SnapshotTreeCapture> BuildSnapshotTreeForCapturedHeadAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? expectedBranchTip,
        CancellationToken cancellationToken)
    {
        using HeadOwnershipLease headLease = await AcquireSnapshotHeadLeaseAsync(
                repository,
                runner,
                branchRef,
                expectedBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        string indexPath = await ResolveGitPathAsync(
                repository,
                runner,
                "index",
                cancellationToken)
            .ConfigureAwait(false);
        IndexFileSnapshot index = await CaptureIndexFileSnapshotAsync(
                indexPath,
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotTreeBuildResult tree = await BuildSnapshotTreeAsync(
                repository,
                runner,
                expectedBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        return new SnapshotTreeCapture(
            tree.Tree,
            indexPath,
            index,
            tree.TemporaryPathspecsToReconcile);
    }

    private async Task<HeadOwnershipLease> AcquireSnapshotHeadLeaseAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? expectedBranchTip,
        CancellationToken cancellationToken)
    {
        string headPath = await ResolveGitPathAsync(
                repository,
                runner,
                "HEAD",
                cancellationToken)
            .ConfigureAwait(false);
        HeadOwnershipLease lease = HeadOwnershipLease.Acquire(
            headPath,
            branchRef,
            ex => LogWarningBestEffort(
                ex,
                "Failed to release the protected Git HEAD lock after a snapshot operation."));
        try
        {
            string? currentBranchTip = await TryResolveCommitAsync(
                    repository,
                    runner,
                    branchRef,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    currentBranchTip,
                    expectedBranchTip,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ProjectCheckpointStateChangedException();
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static async Task EnsureSnapshotTreeContainsNoGitlinksAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        CancellationToken cancellationToken)
    {
        GitCommandResult entries = await runner.RunAsync(
                repository,
                ["ls-tree", "-r", "-z", tree, "--", repository.Pathspec],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxSnapshotTreeInspectionBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "Git could not safely inspect the complete project tree for nested repositories.");
        }

        string? gitlink = GitCliRunner.SplitNullSeparated(entries.Stdout)
            .FirstOrDefault(static entry => entry.StartsWith("160000 ", StringComparison.Ordinal));
        if (gitlink is null)
        {
            return;
        }

        int pathSeparator = gitlink.IndexOf('\t');
        string path = pathSeparator >= 0 ? gitlink[(pathSeparator + 1)..] : gitlink;
        throw new InvalidOperationException(
            $"The nested Git repository '{path}' cannot be snapshotted safely.");
    }

    private async Task<SnapshotCommit?> CreateSnapshotCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        string? parentCommit,
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-hook-index-{Guid.NewGuid():N}");
        string? messagePath = null;
        bool retainMessage = false;

        try
        {
            SnapshotIdentity author = await ResolveSnapshotIdentityAsync(
                    repository,
                    runner,
                    "GIT_AUTHOR_IDENT",
                    "author",
                    cancellationToken)
                .ConfigureAwait(false);
            SnapshotIdentity committer = await ResolveSnapshotIdentityAsync(
                    repository,
                    runner,
                    "GIT_COMMITTER_IDENT",
                    "committer",
                    cancellationToken)
                .ConfigureAwait(false);
            CommitCleanupMode cleanupMode = await ResolveCommitCleanupModeAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            bool signCommit = kind == SnapshotKind.Manual
                              && await IsCommitSigningEnabledAsync(
                                      repository,
                                      runner,
                                      cancellationToken)
                                  .ConfigureAwait(false);
            string standardMessagePath = await ResolveGitPathAsync(
                    repository,
                    runner,
                    "COMMIT_EDITMSG",
                    cancellationToken)
                .ConfigureAwait(false);
            messagePath = Path.Combine(
                Path.GetDirectoryName(standardMessagePath)
                ?? throw new InvalidOperationException(
                    "The Git commit-message path has no parent directory."),
                $"beutl-commit-message-{Guid.NewGuid():N}.tmp");
            var hookEnvironment = new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
                ["GIT_EDITOR"] = ":",
                ["GIT_COMMIT_EDITMSG"] = messagePath,
                ["GIT_AUTHOR_NAME"] = author.Name,
                ["GIT_AUTHOR_EMAIL"] = author.Email,
                ["GIT_AUTHOR_DATE"] = author.Date,
                ["GIT_COMMITTER_NAME"] = committer.Name,
                ["GIT_COMMITTER_EMAIL"] = committer.Email,
                ["GIT_COMMITTER_DATE"] = committer.Date,
            };
            var hookOptions = new GitCommandOptions(
                GitCommandExecutionKind.Local,
                hookEnvironment);
            byte[] initialMessage = await StripCommitMessageAsync(
                    repository,
                    runner,
                    new UTF8Encoding(false).GetBytes(
                        CreateSnapshotCommitMessage(message, kind)),
                    cleanupMode == CommitCleanupMode.Verbatim
                        ? CommitCleanupMode.Verbatim
                        : CommitCleanupMode.Whitespace,
                    commentChar: null,
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            char commentChar = cleanupMode == CommitCleanupMode.Strip
                ? await ResolveCommitCommentCharAsync(
                        repository,
                        runner,
                        initialMessage,
                        cancellationToken)
                    .ConfigureAwait(false)
                : '#';
            await runner.RunAsync(
                    repository,
                    ["read-tree", tree],
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            await RunCommitHookAsync(
                    repository,
                    runner,
                    "pre-commit",
                    [],
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            await WriteCommitMessageAsync(
                    messagePath,
                    initialMessage,
                    createNew: true,
                    cancellationToken)
                .ConfigureAwait(false);
            await RunCommitHookAsync(
                    repository,
                    runner,
                    "prepare-commit-msg",
                    [messagePath, "message"],
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            await RunCommitHookAsync(
                    repository,
                    runner,
                    "commit-msg",
                    [messagePath],
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            byte[] finalMessage = await StripCommitMessageAsync(
                    repository,
                    runner,
                    await ReadCommitMessageAsync(messagePath, cancellationToken)
                        .ConfigureAwait(false),
                    cleanupMode,
                    commentChar,
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (IsEmptyCommitMessage(finalMessage))
            {
                throw new InvalidOperationException(
                    "The snapshot commit message was empty after commit hooks ran.");
            }

            finalMessage = await EnsureSnapshotTrailerAsync(
                    repository,
                    runner,
                    finalMessage,
                    kind,
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            await WriteCommitMessageAsync(
                    messagePath,
                    finalMessage,
                    createNew: false,
                    cancellationToken)
                .ConfigureAwait(false);

            GitCommandResult hookTreeResult = await runner.RunAsync(
                    repository,
                    ["write-tree"],
                    hookOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            string hookTree = hookTreeResult.Stdout.Trim();
            GitRevisionValidator.ValidateCommitId(hookTree, nameof(hookTree));
            await ValidateHookModifiedSnapshotTreeAsync(
                    repository,
                    runner,
                    tree,
                    hookTree,
                    cancellationToken)
                .ConfigureAwait(false);
            bool isEmptyCommit;
            if (parentCommit is null)
            {
                GitCommandResult entries = await runner.RunAsync(
                        repository,
                        ["ls-tree", "-r", "-z", hookTree],
                        GitCommandOptions.Local with { MaxStdoutBytes = 1 },
                        cancellationToken)
                    .ConfigureAwait(false);
                isEmptyCommit = !entries.StdoutTruncated && entries.Stdout.Length == 0;
            }
            else
            {
                string parentTree = await ResolveTreeAsync(
                        repository,
                        runner,
                        parentCommit,
                        cancellationToken)
                    .ConfigureAwait(false);
                isEmptyCommit = string.Equals(
                    hookTree,
                    parentTree,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (isEmptyCommit)
            {
                if (kind == SnapshotKind.Init)
                {
                    throw new InvalidOperationException(
                        "A commit hook removed every change from the initial snapshot.");
                }

                return null;
            }

            var arguments = new List<string>
            {
                "commit-tree",
                hookTree,
            };
            if (parentCommit is not null)
            {
                arguments.Add("-p");
                arguments.Add(parentCommit);
            }

            if (signCommit)
            {
                arguments.Add("-S");
            }
            else if (kind != SnapshotKind.Manual)
            {
                arguments.Add("--no-gpg-sign");
            }

            arguments.Add("-F");
            arguments.Add(messagePath);

            cancellationToken.ThrowIfCancellationRequested();
            GitCommandResult commit = await runner.RunAsync(
                    repository,
                    arguments,
                    hookOptions,
                    CancellationToken.None)
                .ConfigureAwait(false);
            string commitId = commit.Stdout.Trim();
            GitRevisionValidator.ValidateCommitId(commitId, nameof(commitId));
            retainMessage = true;
            return new SnapshotCommit(
                commitId,
                hookTree,
                messagePath,
                author,
                committer);
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
            if (!retainMessage && messagePath is not null)
            {
                TryDeleteTemporaryIndex(messagePath);
            }
        }
    }

    private static string CreateSnapshotCommitMessage(string message, SnapshotKind kind)
    {
        return $"{message}\n\nBeutl-Snapshot: {kind.ToString().ToLowerInvariant()}\n";
    }

    private static async Task<byte[]> ReadCommitMessageAsync(
        string messagePath,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(messagePath);
        file.Refresh();
        if (!file.Exists || file.Length > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit hook produced an invalid commit message file.");
        }

        await using var stream = new FileStream(
            messagePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        var contents = new byte[MaxCommitMessageBytes + 1];
        int count = 0;
        while (count < contents.Length)
        {
            int read = await stream.ReadAsync(contents.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit hook produced an invalid commit message file.");
        }

        Array.Resize(ref contents, count);
        return contents;
    }

    private static async Task WriteCommitMessageAsync(
        string messagePath,
        byte[] contents,
        bool createNew,
        CancellationToken cancellationToken)
    {
        if (contents.Length > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        await using var stream = new FileStream(
            messagePath,
            new FileStreamOptions
            {
                Mode = createNew ? FileMode.CreateNew : FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            });
        await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> StripCommitMessageAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] message,
        CommitCleanupMode cleanupMode,
        char? commentChar,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (message.Length > MaxCommitMessageBytes)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        if (cleanupMode == CommitCleanupMode.Verbatim)
        {
            return message;
        }

        IReadOnlyList<string> arguments = cleanupMode == CommitCleanupMode.Strip
            ? ["-c", $"core.commentChar={commentChar ?? '#'}", "stripspace", "--strip-comments"]
            : ["stripspace"];
        GitCommandResult stripped = await runner.RunAsync(
                repository,
                arguments,
                options with
                {
                    MaxStdoutBytes = MaxCommitMessageBytes,
                    StandardInputBytes = message,
                    CaptureStdoutBytes = true,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (stripped.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        return stripped.StdoutBytes
               ?? throw new InvalidOperationException(
                   "Git did not return the cleaned snapshot commit message bytes.");
    }

    private static async Task<byte[]> EnsureSnapshotTrailerAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] message,
        SnapshotKind kind,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        string expectedValue = kind.ToString().ToLowerInvariant();
        (int count, bool matchesExpectedValue, byte[] otherTrailers)
            = await ParseSnapshotTrailersAsync(
                repository,
                runner,
                message,
                expectedValue,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        if (count == 1 && matchesExpectedValue)
        {
            return message;
        }

        byte[] canonicalTrailer = Encoding.UTF8.GetBytes(
            $"Beutl-Snapshot: {expectedValue}\n");
        int interTrailerNewline = otherTrailers.Length > 0
                                 && otherTrailers[^1] != (byte)'\n'
            ? 1
            : 0;
        int appendedLength = 2
                             + otherTrailers.Length
                             + interTrailerNewline
                             + canonicalTrailer.Length;
        if (message.Length > MaxCommitMessageBytes - appendedLength)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        var reconstructed = new byte[message.Length + appendedLength];
        int offset = 0;
        message.CopyTo(reconstructed, offset);
        offset += message.Length;
        reconstructed[offset++] = (byte)'\n';
        reconstructed[offset++] = (byte)'\n';
        otherTrailers.CopyTo(reconstructed, offset);
        offset += otherTrailers.Length;
        if (interTrailerNewline != 0)
        {
            reconstructed[offset++] = (byte)'\n';
        }

        canonicalTrailer.CopyTo(reconstructed, offset);
        (int reconstructedCount, bool reconstructedMatches, byte[] reconstructedOtherTrailers)
            = await ParseSnapshotTrailersAsync(
                repository,
                runner,
                reconstructed,
                expectedValue,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        if (reconstructedCount != 1
            || !reconstructedMatches
            || !reconstructedOtherTrailers.AsSpan().SequenceEqual(otherTrailers))
        {
            throw new InvalidOperationException(
                "The snapshot commit message did not retain its required trailer after commit hooks ran.");
        }

        return reconstructed;
    }

    private static async Task<(int Count, bool MatchesExpectedValue, byte[] OtherTrailers)>
        ParseSnapshotTrailersAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] message,
        string expectedValue,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        GitCommandResult parsed = await runner.RunAsync(
                repository,
                [
                    "-c",
                    "trailer.separators=:=",
                    "interpret-trailers",
                    "--parse",
                    "--no-divider",
                ],
                options with
                {
                    MaxStdoutBytes = MaxCommitMessageBytes,
                    StandardInputBytes = message,
                    CaptureStdoutBytes = true,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (parsed.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The snapshot commit message exceeded its safety limit.");
        }

        return ParseSnapshotTrailerBytes(
            parsed.StdoutBytes
            ?? throw new InvalidOperationException(
                "Git did not return the parsed snapshot commit trailers."),
            expectedValue);
    }

    private static (int Count, bool MatchesExpectedValue, byte[] OtherTrailers)
        ParseSnapshotTrailerBytes(byte[] trailers, string expectedValue)
    {
        const string SnapshotTrailerToken = "Beutl-Snapshot";
        int snapshotCount = 0;
        bool matchesExpectedValue = false;
        using var otherTrailers = new MemoryStream(trailers.Length);
        int offset = 0;
        while (offset < trailers.Length)
        {
            int newline = Array.IndexOf(trailers, (byte)'\n', offset);
            int recordEnd = newline >= 0 ? newline + 1 : trailers.Length;
            int contentLength = (newline >= 0 ? newline : trailers.Length) - offset;
            if (contentLength > 0 && trailers[offset + contentLength - 1] == (byte)'\r')
            {
                contentLength--;
            }

            string line = Encoding.UTF8.GetString(trailers, offset, contentLength);
            int separator = line.IndexOf(':');
            bool isSnapshotTrailer = separator >= 0
                                     && string.Equals(
                                         line[..separator].Trim(),
                                         SnapshotTrailerToken,
                                         StringComparison.OrdinalIgnoreCase);
            if (isSnapshotTrailer)
            {
                snapshotCount++;
                matchesExpectedValue = string.Equals(
                    line[(separator + 1)..].Trim(),
                    expectedValue,
                    StringComparison.Ordinal);
            }
            else
            {
                otherTrailers.Write(trailers, offset, recordEnd - offset);
            }

            offset = recordEnd;
        }

        return (
            snapshotCount,
            snapshotCount == 1 && matchesExpectedValue,
            otherTrailers.ToArray());
    }

    private static bool IsEmptyCommitMessage(byte[] message)
    {
        // Git runs under LC_ALL=C and treats only ASCII whitespace as empty. Non-ASCII bytes are
        // message content regardless of i18n.commitEncoding.
        return message.All(static value => value is (byte)' '
            or (byte)'\t'
            or (byte)'\r'
            or (byte)'\n'
            or (byte)'\v'
            or (byte)'\f');
    }

    private static async Task<char> ResolveCommitCommentCharAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        byte[] initialMessage,
        CancellationToken cancellationToken)
    {
        string configured;
        try
        {
            GitCommandResult result = await runner.RunAsync(
                    repository,
                    ["config", "--get", "core.commentChar"],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            configured = result.Stdout.TrimEnd('\r', '\n');
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return '#';
        }

        if (!string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (configured.Length != 1 || char.IsControl(configured[0]))
            {
                throw new InvalidOperationException(
                    "Git returned an invalid core.commentChar value.");
            }

            return configured[0];
        }

        const string Candidates = "#;@!$%^&|:";
        foreach (char candidate in Candidates)
        {
            byte candidateByte = checked((byte)candidate);
            bool startsLine = false;
            for (int i = 0; i < initialMessage.Length; i++)
            {
                if ((i == 0 || initialMessage[i - 1] == (byte)'\n')
                    && initialMessage[i] == candidateByte)
                {
                    startsLine = true;
                    break;
                }
            }

            if (!startsLine)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Git could not select an automatic commit comment character.");
    }

    private static async Task<CommitCleanupMode> ResolveCommitCleanupModeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                    repository,
                    ["config", "--get", "commit.cleanup"],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Stdout.Trim().ToLowerInvariant() switch
            {
                "" or "default" or "whitespace" or "scissors" =>
                    CommitCleanupMode.Whitespace,
                "strip" => CommitCleanupMode.Strip,
                "verbatim" => CommitCleanupMode.Verbatim,
                var value => throw new InvalidOperationException(
                    $"Git returned an unsupported commit.cleanup value: '{value}'."),
            };
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return CommitCleanupMode.Whitespace;
        }
    }

    private static async Task<SnapshotIdentity> ResolveSnapshotIdentityAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string variable,
        string description,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                ["var", variable],
                GitCommandOptions.Local,
                cancellationToken)
            .ConfigureAwait(false);
        string ident = result.Stdout.TrimEnd('\r', '\n');
        int closeBracket = ident.LastIndexOf('>');
        int openBracket = closeBracket < 0
            ? -1
            : ident.LastIndexOf('<', closeBracket);
        string name = openBracket <= 0 ? string.Empty : ident[..openBracket].TrimEnd();
        string email = openBracket < 0 || closeBracket <= openBracket
            ? string.Empty
            : ident[(openBracket + 1)..closeBracket];
        string date = closeBracket < 0 ? string.Empty : ident[(closeBracket + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(date)
            || name.Any(char.IsControl)
            || email.Any(char.IsControl)
            || date.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Git returned an invalid snapshot {description} identity.");
        }

        return new SnapshotIdentity(name, email, date);
    }

    private static Task RunCommitHookAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string hookName,
        IReadOnlyList<string> hookArguments,
        GitCommandOptions options,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "hook",
            "run",
            "--ignore-missing",
            hookName,
        };
        if (hookArguments.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(hookArguments);
        }

        return RunHookCoreAsync();

        async Task RunHookCoreAsync()
        {
            await runner.RunAsync(
                    repository,
                    arguments,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RunPostCommitHookBestEffortAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        SnapshotCommit commit,
        string indexPath)
    {
        try
        {
            await RunCommitHookAsync(
                    repository,
                    runner,
                    "post-commit",
                    [],
                    new GitCommandOptions(
                        GitCommandExecutionKind.Local,
                        new Dictionary<string, string?>
                        {
                            ["GIT_EDITOR"] = ":",
                            ["GIT_INDEX_FILE"] = indexPath,
                            ["GIT_COMMIT_EDITMSG"] = commit.MessagePath,
                            ["GIT_AUTHOR_NAME"] = commit.Author.Name,
                            ["GIT_AUTHOR_EMAIL"] = commit.Author.Email,
                            ["GIT_AUTHOR_DATE"] = commit.Author.Date,
                            ["GIT_COMMITTER_NAME"] = commit.Committer.Name,
                            ["GIT_COMMITTER_EMAIL"] = commit.Committer.Email,
                            ["GIT_COMMITTER_DATE"] = commit.Committer.Date,
                        }),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // post-commit cannot reject a commit that is already durable. Match Git's one-way
            // lifecycle: report the hook failure for diagnostics without making callers retry.
            LogWarningBestEffort(
                ex,
                "The post-commit hook failed after the snapshot commit became durable.");
        }
    }

    private bool ReleaseSnapshotHeadLeaseForPostCommit(HeadOwnershipLease headLease)
    {
        try
        {
            headLease.VerifyStillOwned();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogWarningBestEffort(
                ex,
                "The post-commit hook was skipped because the checked-out branch changed after the snapshot became durable.");
            return false;
        }
        finally
        {
            headLease.Dispose();
        }
    }

    private async Task ValidateHookModifiedSnapshotTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string originalTree,
        string hookTree,
        CancellationToken cancellationToken)
    {
        await EnsureSnapshotTreeContainsNoGitlinksAsync(
                repository,
                runner,
                hookTree,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(originalTree, hookTree, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        GitCommandResult changed = await runner.RunAsync(
                repository,
                [
                    "diff-tree",
                    "--no-commit-id",
                    "--name-only",
                    "-r",
                    "-z",
                    originalTree,
                    hookTree,
                ],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxSnapshotTreeInspectionBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<string> changedPaths = GitCliRunner.SplitNullSeparated(changed.Stdout);
        if (changed.StdoutTruncated
            || changedPaths.Any(path => !IsHookWritableSnapshotPath(repository, path)))
        {
            throw new InvalidOperationException(
                "A commit hook changed content outside the safe project snapshot scope.");
        }

        GitCommandResult entries = await runner.RunAsync(
                repository,
                ["ls-tree", "-r", "-z", hookTree, "--", repository.Pathspec],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxSnapshotTreeInspectionBytes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "A commit hook produced a project tree that exceeded its safety limit.");
        }

        var finalModes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string entry in GitCliRunner.SplitNullSeparated(entries.Stdout))
        {
            int metadataSeparator = entry.IndexOf('\t');
            int modeSeparator = entry.IndexOf(' ');
            if (metadataSeparator <= 0 || modeSeparator <= 0 || modeSeparator > metadataSeparator)
            {
                throw new InvalidOperationException(
                    "A commit hook produced an invalid project tree entry.");
            }

            finalModes[entry[(metadataSeparator + 1)..]] = entry[..modeSeparator];
        }

        if (changedPaths.Any(path =>
                !finalModes.TryGetValue(path, out string? mode)
                || mode is not ("100644" or "100755")))
        {
            throw new InvalidOperationException(
                "A commit hook removed content or introduced a non-regular project tree entry.");
        }

        await ValidateFinalHookProjectGraphAsync(
                repository,
                runner,
                hookTree,
                finalModes,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsHookWritableSnapshotPath(
        RepositoryInfo repository,
        string repositoryRelativePath)
    {
        string projectRelativePath;
        if (repository.Pathspec == ".")
        {
            projectRelativePath = repositoryRelativePath;
        }
        else
        {
            string prefix = repository.Pathspec + "/";
            if (!repositoryRelativePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            projectRelativePath = repositoryRelativePath[prefix.Length..];
        }

        if (projectRelativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => string.Equals(
                segment,
                ".beutl",
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !projectRelativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
               || _requiredTemporaryProjectPaths.Any(requiredPath =>
                   AreSameProjectRelativePath(
                       repository.ProjectRoot,
                       requiredPath,
                       projectRelativePath));
    }

    private async Task ValidateFinalHookProjectGraphAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string hookTree,
        IReadOnlyDictionary<string, string> finalModes,
        CancellationToken cancellationToken)
    {
        if (_projectFile is null
            || !RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, _projectFile))
        {
            return;
        }

        string projectFileRepositoryPath = GetRepositoryRelativeProjectFilePath(
            repository,
            _projectFile);
        if (!finalModes.TryGetValue(projectFileRepositoryPath, out string? projectMode)
            || projectMode is not ("100644" or "100755"))
        {
            throw new InvalidOperationException(
                "A commit hook removed the project file from the snapshot tree.");
        }

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"beutl-hook-graph-{Guid.NewGuid():N}");
        string materializedRepositoryRoot = Path.Combine(temporaryRoot, "tree");
        try
        {
            Directory.CreateDirectory(materializedRepositoryRoot);
            Dictionary<string, long> graphFiles = await ListHistoricalGraphFilesAsync(
                    repository,
                    runner,
                    hookTree,
                    projectFileRepositoryPath,
                    cancellationToken)
                .ConfigureAwait(false);
            await MaterializeHistoricalGraphFilesAsync(
                    repository,
                    runner,
                    hookTree,
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
            string[] finalTemporaryPaths = serializedPaths
                .Where(static path => path.EndsWith(
                    ".tmp",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (finalTemporaryPaths.Length != _requiredTemporaryProjectPaths.Count
                || finalTemporaryPaths.Any(finalPath =>
                    !_requiredTemporaryProjectPaths.Any(currentPath =>
                        AreSameProjectRelativePath(
                            repository.ProjectRoot,
                            finalPath,
                            currentPath)))
                || _requiredTemporaryProjectPaths.Any(currentPath =>
                    !finalTemporaryPaths.Any(finalPath =>
                        AreSameProjectRelativePath(
                            repository.ProjectRoot,
                            currentPath,
                            finalPath))))
            {
                throw new InvalidOperationException(
                    "A commit hook changed the set of required temporary project files.");
            }

            foreach (string projectRelativePath in serializedPaths)
            {
                string repositoryRelativePath = repository.Pathspec == "."
                    ? projectRelativePath
                    : repository.Pathspec + "/" + projectRelativePath;
                if (!finalModes.TryGetValue(repositoryRelativePath, out string? mode)
                    || mode is not ("100644" or "100755"))
                {
                    throw new InvalidOperationException(
                        $"A commit hook left required project content '{projectRelativePath}' out of the snapshot tree.");
                }
            }
        }
        finally
        {
            TryDeleteHistoricalGraphDirectory(temporaryRoot);
        }
    }

    private static async Task<bool> IsCommitSigningEnabledAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                    repository,
                    ["config", "--bool", "--get", "commit.gpgSign"],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Stdout.Trim() switch
            {
                "true" => true,
                "false" or "" => false,
                var value => throw new InvalidOperationException(
                    $"Git returned an invalid commit.gpgSign value: '{value}'."),
            };
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private async Task PublishSnapshotCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? expectedOldCommit,
        string commit,
        string reflogMessage)
    {
        try
        {
            await runner.RunAsync(
                    repository,
                    [
                        "update-ref",
                        "--create-reflog",
                        "-m",
                        reflogMessage,
                        branchRef,
                        commit,
                        expectedOldCommit ?? string.Empty,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }
        catch (Exception publicationException)
        {
            string? observedCommit;
            try
            {
                observedCommit = await TryResolveCommitWithRetryAsync(
                        repository,
                        runner,
                        branchRef)
                    .ConfigureAwait(false);
            }
            catch (Exception observationException)
            {
                throw new AggregateException(
                    $"The snapshot ref '{branchRef}' could not be published or observed safely.",
                    publicationException,
                    observationException);
            }

            if (string.Equals(observedCommit, commit, StringComparison.OrdinalIgnoreCase))
            {
                LogWarningBestEffort(
                    publicationException,
                    "Git reported a snapshot publication failure after the captured branch was updated.");
                return;
            }

            if (string.Equals(
                    observedCommit,
                    expectedOldCommit,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }

            throw new AggregateException(
                $"The captured branch '{branchRef}' changed before the snapshot could be published.",
                publicationException,
                new ProjectCheckpointStateChangedException());
        }
    }

    private async Task PublishSnapshotAndReconcileIndexAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string branchRef,
        string? expectedOldCommit,
        string commit,
        SnapshotTreeCapture snapshot,
        HeadOwnershipLease headLease,
        string reflogMessage,
        CancellationToken cancellationToken)
    {
        string refUpdateWorktreePath = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-ref-update-{Guid.NewGuid():N}");
        var refUpdateRepository = new RepositoryInfo(
            refUpdateWorktreePath,
            refUpdateWorktreePath);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await runner.RunAsync(
                    repository,
                    [
                        "worktree",
                        "add",
                        "--detach",
                        "--no-checkout",
                        refUpdateWorktreePath,
                        commit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None)
                .ConfigureAwait(false);

            headLease.VerifyStillOwned();
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            IndexFileSnapshot indexAfter = await TransformIndexSnapshotAsync(
                    repository,
                    runner,
                    snapshot.IndexPath,
                    snapshot.Index,
                    CreateSnapshotIndexReconciliationCommands(
                        repository,
                        commit,
                        snapshot.TemporaryPathspecsToReconcile),
                    new GitCommandOptions(GitCommandExecutionKind.Local)
                    {
                        UseLiteralPathspecs = false,
                    },
                    $"The Git index '{snapshot.IndexPath}' changed after the snapshot tree was captured; the live index was left untouched.",
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                headLease.VerifyStillOwned();
                await PublishSnapshotCommitAsync(
                        refUpdateRepository,
                        runner,
                        branchRef,
                        expectedOldCommit,
                        commit,
                        reflogMessage)
                    .ConfigureAwait(false);
            }
            catch (Exception publicationException)
            {
                try
                {
                    await RestoreFailedIndexSnapshotAsync(
                            snapshot.IndexPath,
                            snapshot.Index,
                            indexAfter)
                        .ConfigureAwait(false);
                }
                catch (Exception restoreException)
                {
                    throw new AggregateException(
                        "The snapshot could not be published and the prior index could not be restored.",
                        publicationException,
                        restoreException);
                }

                throw;
            }

            CacheHistoricalRequiredTemporaryPaths(
                commit,
                _requiredTemporaryProjectPaths);
        }
        finally
        {
            await RemoveRefUpdateWorktreeBestEffortAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath)
                .ConfigureAwait(false);
        }
    }

    private static async Task<WorktreeStateFingerprint> CaptureWorktreeStateAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string baseCommit,
        string pathspec,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["add", "-A", "--", pathspec],
                indexOptions with { ExecutionKind = GitCommandExecutionKind.LocalWithLfs },
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult indexEntries = await runner.RunAsync(
                repository,
                ["ls-files", "--stage", "-z", "--", pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return new WorktreeStateFingerprint(tree.Stdout.Trim(), indexEntries.Stdout);
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<string> BuildProjectTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string baseCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", baseCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                [
                    "restore",
                    $"--source={sourceCommit}",
                    "--staged",
                    "--",
                    repository.Pathspec,
                ],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            return tree.Stdout.Trim();
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<string> BuildMergedTreeAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string mergeBase,
        string currentCommit,
        string incomingCommit,
        CancellationToken cancellationToken)
    {
        string temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-index-{Guid.NewGuid():N}");
        var indexOptions = new GitCommandOptions(
            GitCommandExecutionKind.Local,
            new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = temporaryIndex,
            });

        try
        {
            await runner.RunAsync(
                repository,
                ["read-tree", "-m", mergeBase, currentCommit, incomingCommit],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult tree = await runner.RunAsync(
                repository,
                ["write-tree"],
                indexOptions,
                cancellationToken).ConfigureAwait(false);
            return tree.Stdout.Trim();
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndex);
        }
    }

    private static async Task<bool> IsWholeRepositoryCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            ["status", "--porcelain=v1", "--untracked-files=all", "-z"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return result.Stdout.Length == 0;
    }

    private static async Task<string?> FindIgnoredIncomingPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string currentCommit,
        string incomingCommit,
        CancellationToken cancellationToken)
    {
        GitCommandResult changed = await runner.RunAsync(
            repository,
            [
                "diff",
                "--name-only",
                "--diff-filter=ACR",
                "-z",
                currentCommit,
                incomingCommit,
                "--",
                ".",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> changedPaths = GitCliRunner.SplitNullSeparated(changed.Stdout);
        string repositoryRoot = Path.GetFullPath(repository.RepoRoot);
        string[] existingPaths = changedPaths
            .Where(path =>
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, path));
                }
                catch (Exception ex) when (ex is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
                {
                    return false;
                }

                return VersionControlPathComparison.IsSameOrDescendant(repositoryRoot, fullPath)
                       && (File.Exists(fullPath) || Directory.Exists(fullPath));
            })
            .ToArray();
        if (existingPaths.Length == 0)
        {
            return null;
        }

        string input = string.Join('\0', existingPaths) + '\0';
        try
        {
            GitCommandResult ignored = await runner.RunAsync(
                repository,
                ["check-ignore", "--stdin", "-z"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    StandardInput: input,
                    UseLiteralPathspecs: false),
                cancellationToken).ConfigureAwait(false);
            return GitCliRunner.SplitNullSeparated(ignored.Stdout).FirstOrDefault();
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }

    private static async Task<bool> IsProjectCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "status",
                "--porcelain=v1",
                "--untracked-files=all",
                "-z",
                "--",
                repository.Pathspec,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return result.Stdout.Length == 0;
    }

    private static async Task<bool> IsProjectIndexCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["diff", "--cached", "--quiet", "--", repository.Pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static async Task<bool> IsWholeIndexCleanAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["diff", "--cached", "--quiet", "--", "."],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static async Task<bool> IsAncestorAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string ancestor,
        string descendant,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["merge-base", "--is-ancestor", ancestor, descendant],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static async Task<PullRelation> GetPullRelationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string localCommit,
        string upstreamCommit,
        CancellationToken cancellationToken)
    {
        if (string.Equals(localCommit, upstreamCommit, StringComparison.OrdinalIgnoreCase))
        {
            return PullRelation.Equal;
        }

        if (await IsAncestorAsync(
                repository,
                runner,
                localCommit,
                upstreamCommit,
                cancellationToken).ConfigureAwait(false))
        {
            return PullRelation.LocalBehind;
        }

        return await IsAncestorAsync(
                repository,
                runner,
                upstreamCommit,
                localCommit,
                cancellationToken).ConfigureAwait(false)
            ? PullRelation.LocalAhead
            : PullRelation.Diverged;
    }

    private async Task<TreeTransitionResult> ApplyTreeTransitionAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip currentHead,
        CheckedOutBranchTip targetHead,
        string currentTreeCommit,
        string targetTreeCommit,
        string reflogMessage,
        TreeTransitionIndexPlan? indexPlan,
        CancellationToken cancellationToken,
        Action? validatePreparedTarget = null)
    {
        if (!string.Equals(currentHead.RefName, targetHead.RefName, StringComparison.Ordinal))
        {
            throw new ArgumentException("A tree transition must remain on the same local branch.");
        }

        string headPath = await ResolveGitPathAsync(
                repository,
                runner,
                "HEAD",
                cancellationToken)
            .ConfigureAwait(false);
        string indexPath = await ResolveGitPathAsync(
                repository,
                runner,
                "index",
                cancellationToken)
            .ConfigureAwait(false);
        string refUpdateWorktreePath = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-ref-update-{Guid.NewGuid():N}");
        try
        {
            await runner.RunAsync(
                repository,
                [
                    "worktree",
                    "add",
                    "--detach",
                    "--no-checkout",
                    refUpdateWorktreePath,
                    currentTreeCommit,
                ],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RemoveRefUpdateWorktreeBestEffortAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath)
                .ConfigureAwait(false);
            return new TreeTransitionResult(
                TreeTransitionOutcome.RestoredCurrent,
                ex,
                currentHead);
        }

        var refUpdateRepository = new RepositoryInfo(
            refUpdateWorktreePath,
            refUpdateWorktreePath);
        var transitionCheckoutOptions = new GitCommandOptions(
            GitCommandExecutionKind.LocalWithLfs,
            new Dictionary<string, string?>
            {
                ["GIT_WORK_TREE"] = repository.RepoRoot,
                ["GIT_INDEX_FILE"] = indexPath,
            });
        bool mutationStarted = false;
        try
        {
            using HeadOwnershipLease lease = HeadOwnershipLease.Acquire(
                headPath,
                currentHead.RefName,
                ex => LogWarningBestEffort(
                    ex,
                    "Failed to release the protected Git HEAD lock."));
            CheckedOutBranchTip actualHead = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(actualHead, currentHead))
            {
                return new TreeTransitionResult(
                    TreeTransitionOutcome.OwnershipLost,
                    ActualTip: actualHead);
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);

            WorktreeStateFingerprint originalState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    currentTreeCommit,
                    indexPlan?.Pathspec ?? ".",
                    CancellationToken.None)
                .ConfigureAwait(false);
            string currentTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    currentTreeCommit,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!string.Equals(originalState.Tree, currentTree, StringComparison.OrdinalIgnoreCase))
            {
                return new TreeTransitionResult(TreeTransitionOutcome.OwnershipLost);
            }

            WorktreeStateFingerprint preparedState = originalState;
            bool worktreeMutationAttempted = false;
            bool targetPrepared = false;
            try
            {
                if (indexPlan?.PrepareCommit is { } prepareCommit)
                {
                    await EnsureNoExternalRepositoryOperationAsync(
                            repository,
                            runner,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    mutationStarted = true;
                    await ResetIndexAsync(
                            repository,
                            runner,
                            prepareCommit,
                            indexPlan.Pathspec)
                        .ConfigureAwait(false);
                    preparedState = await CaptureWorktreeStateAsync(
                            repository,
                            runner,
                            currentTreeCommit,
                            indexPlan.Pathspec,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }

                string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                        repository,
                        runner,
                        currentTreeCommit,
                        targetTreeCommit,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (ignoredCollision is not null)
                {
                    throw new InvalidOperationException(
                        $"The tree transition would overwrite the ignored path '{ignoredCollision}'.");
                }

                await EnsureNoExternalRepositoryOperationAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                mutationStarted = true;
                worktreeMutationAttempted = true;
                await runner.RunAsync(
                    refUpdateRepository,
                    [
                        .. s_lfsPathFilterOverrides,
                        "-c",
                        "core.hooksPath=/dev/null",
                        "checkout",
                        "--detach",
                        "--no-overwrite-ignore",
                        targetTreeCommit,
                    ],
                    transitionCheckoutOptions,
                    CancellationToken.None).ConfigureAwait(false);

                if (indexPlan?.FinalCommit is { } finalCommit)
                {
                    await EnsureNoExternalRepositoryOperationAsync(
                            repository,
                            runner,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    await ResetIndexAsync(
                            repository,
                            runner,
                            finalCommit,
                            indexPlan.Pathspec)
                        .ConfigureAwait(false);
                }

                WorktreeStateFingerprint targetState = await CaptureWorktreeStateAsync(
                        repository,
                        runner,
                        targetTreeCommit,
                        indexPlan?.Pathspec ?? ".",
                        CancellationToken.None)
                    .ConfigureAwait(false);
                string targetTree = await ResolveTreeAsync(
                        repository,
                        runner,
                        targetTreeCommit,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                string expectedIndexCommit = indexPlan?.FinalCommit ?? targetTreeCommit;
                if (!string.Equals(targetState.Tree, targetTree, StringComparison.OrdinalIgnoreCase)
                    || !await IsIndexAtCommitAsync(
                            repository,
                            runner,
                            expectedIndexCommit,
                            indexPlan?.Pathspec ?? ".",
                            CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    throw new ProjectCheckpointStateChangedException();
                }

                validatePreparedTarget?.Invoke();
                targetPrepared = true;

                string? branchCommit = await TryResolveCommitAsync(
                        repository,
                        runner,
                        currentHead.RefName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        branchCommit,
                        currentHead.Commit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        ActualTip: await TryGetCheckedOutBranchTipAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false));
                }

                await EnsureNoExternalRepositoryOperationAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await runner.RunAsync(
                    refUpdateRepository,
                    [
                        "update-ref",
                        "-m",
                        reflogMessage,
                        currentHead.RefName,
                        targetHead.Commit,
                        currentHead.Commit,
                    ],
                    GitCommandOptions.Local,
                    CancellationToken.None).ConfigureAwait(false);
                return new TreeTransitionResult(TreeTransitionOutcome.AppliedTarget);
            }
            catch (Exception transitionException)
            {
                string? branchCommit = await TryResolveCommitAsync(
                        repository,
                        runner,
                        currentHead.RefName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (string.Equals(
                        branchCommit,
                        targetHead.Commit,
                        StringComparison.OrdinalIgnoreCase)
                    && targetPrepared)
                {
                    return new TreeTransitionResult(TreeTransitionOutcome.AppliedTarget);
                }

                if (!string.Equals(
                        branchCommit,
                        currentHead.Commit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        transitionException,
                        await TryGetCheckedOutBranchTipAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false));
                }

                try
                {
                    await EnsureNoExternalRepositoryOperationAsync(
                            repository,
                            runner,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (VersionControlConflictedException externalOperationException)
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        externalOperationException,
                        currentHead);
                }
                catch (Exception recoveryGuardException)
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.RecoveryFailed,
                        new AggregateException(
                            "The tree transition failed and rollback safety could not be established.",
                            transitionException,
                            recoveryGuardException),
                        currentHead);
                }

                try
                {
                    WorktreeStateFingerprint failedState = await CaptureWorktreeStateAsync(
                            repository,
                            runner,
                            currentTreeCommit,
                            indexPlan?.Pathspec ?? ".",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    string targetTree = await ResolveTreeAsync(
                            repository,
                            runner,
                            targetTreeCommit,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    bool worktreeOwned = string.Equals(
                                             failedState.Tree,
                                             originalState.Tree,
                                             StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(
                                             failedState.Tree,
                                             targetTree,
                                             StringComparison.OrdinalIgnoreCase);
                    bool indexOwned = string.Equals(
                                          failedState.IndexEntries,
                                          originalState.IndexEntries,
                                          StringComparison.Ordinal)
                                      || string.Equals(
                                          failedState.IndexEntries,
                                          preparedState.IndexEntries,
                                          StringComparison.Ordinal)
                                      || await IsIndexAtCommitAsync(
                                              repository,
                                              runner,
                                              targetTreeCommit,
                                              indexPlan?.Pathspec ?? ".",
                                              CancellationToken.None)
                                          .ConfigureAwait(false)
                                      || (indexPlan?.PrepareCommit is { } expectedPrepareCommit
                                          && await IsIndexAtCommitAsync(
                                                  repository,
                                                  runner,
                                                  expectedPrepareCommit,
                                                  indexPlan.Pathspec,
                                                  CancellationToken.None)
                                              .ConfigureAwait(false))
                                      || (indexPlan?.FinalCommit is { } expectedFinalCommit
                                          && await IsIndexAtCommitAsync(
                                                  repository,
                                                  runner,
                                                  expectedFinalCommit,
                                                  indexPlan.Pathspec,
                                                  CancellationToken.None)
                                              .ConfigureAwait(false));
                    if (!indexOwned)
                    {
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.OwnershipLost,
                            transitionException,
                            currentHead);
                    }

                    if (!worktreeOwned)
                    {
                        string refusedRestoreCommit = indexPlan?.RestoreCommit ?? currentTreeCommit;
                        await EnsureNoExternalRepositoryOperationAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        await ResetIndexAsync(
                                repository,
                                runner,
                                refusedRestoreCommit,
                                indexPlan?.Pathspec ?? ".")
                            .ConfigureAwait(false);
                        WorktreeStateFingerprint refusedState = await CaptureWorktreeStateAsync(
                                repository,
                                runner,
                                currentTreeCommit,
                                indexPlan?.Pathspec ?? ".",
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        if (!string.Equals(
                                refusedState.IndexEntries,
                                originalState.IndexEntries,
                                StringComparison.Ordinal))
                        {
                            return new TreeTransitionResult(
                                TreeTransitionOutcome.RecoveryFailed,
                                new AggregateException(
                                    "The checkout was refused and the original index could not be restored.",
                                    transitionException),
                                currentHead);
                        }

                        CheckedOutBranchTip? refusedTip = await TryGetCheckedOutBranchTipAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.OwnershipLost,
                            transitionException,
                            refusedTip);
                    }

                    if (worktreeMutationAttempted
                        && string.Equals(
                            failedState.Tree,
                            targetTree,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        string? transitionHead = await TryResolveCommitAsync(
                                refUpdateRepository,
                                runner,
                                "HEAD",
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        if (string.Equals(
                                transitionHead,
                                currentTreeCommit,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                await runner.RunAsync(
                                    refUpdateRepository,
                                    [
                                        "update-ref",
                                        "--no-deref",
                                        "-m",
                                        "beutl align temporary transition head for recovery",
                                        "HEAD",
                                        targetTreeCommit,
                                        currentTreeCommit,
                                    ],
                                    GitCommandOptions.Local,
                                    CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception alignmentException)
                            {
                                transitionHead = await TryResolveCommitAsync(
                                        refUpdateRepository,
                                        runner,
                                        "HEAD",
                                        CancellationToken.None)
                                    .ConfigureAwait(false);
                                if (string.Equals(
                                        transitionHead,
                                        targetTreeCommit,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    // The update reached Git even though the runner lost its response.
                                }
                                else if (string.Equals(
                                             transitionHead,
                                             currentTreeCommit,
                                             StringComparison.OrdinalIgnoreCase))
                                {
                                    return new TreeTransitionResult(
                                        TreeTransitionOutcome.RecoveryFailed,
                                        new AggregateException(
                                            "The temporary transition head could not be aligned for recovery.",
                                            transitionException,
                                            alignmentException),
                                        currentHead);
                                }
                                else
                                {
                                    return new TreeTransitionResult(
                                        TreeTransitionOutcome.OwnershipLost,
                                        new AggregateException(
                                            "The temporary transition head changed while recovery was being prepared.",
                                            transitionException,
                                            alignmentException),
                                        currentHead);
                                }
                            }
                        }
                        else if (!string.Equals(
                                     transitionHead,
                                     targetTreeCommit,
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            return new TreeTransitionResult(
                                TreeTransitionOutcome.OwnershipLost,
                                transitionException,
                                currentHead);
                        }

                        await EnsureNoExternalRepositoryOperationAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        await ResetIndexAsync(
                                repository,
                                runner,
                                targetTreeCommit,
                                indexPlan?.Pathspec ?? ".")
                            .ConfigureAwait(false);
                        await EnsureNoExternalRepositoryOperationAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        await runner.RunAsync(
                            refUpdateRepository,
                            [
                                .. s_lfsPathFilterOverrides,
                                "-c",
                                "core.hooksPath=/dev/null",
                                "checkout",
                                "--detach",
                                "--no-overwrite-ignore",
                                currentTreeCommit,
                            ],
                            transitionCheckoutOptions,
                            CancellationToken.None).ConfigureAwait(false);
                    }

                    if (indexPlan?.RestoreCommit is { } restoreCommit)
                    {
                        await EnsureNoExternalRepositoryOperationAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        await ResetIndexAsync(
                                repository,
                                runner,
                                restoreCommit,
                                indexPlan.Pathspec)
                            .ConfigureAwait(false);
                    }
                    else if (!string.Equals(
                                 failedState.IndexEntries,
                                 originalState.IndexEntries,
                                 StringComparison.Ordinal))
                    {
                        await EnsureNoExternalRepositoryOperationAsync(
                                repository,
                                runner,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        await ResetIndexAsync(
                                repository,
                                runner,
                                currentTreeCommit,
                                indexPlan?.Pathspec ?? ".")
                            .ConfigureAwait(false);
                    }

                    WorktreeStateFingerprint recoveredState = await CaptureWorktreeStateAsync(
                            repository,
                            runner,
                            currentTreeCommit,
                            indexPlan?.Pathspec ?? ".",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    string expectedRestoreCommit = indexPlan?.RestoreCommit ?? currentTreeCommit;
                    if (!string.Equals(
                            recoveredState.Tree,
                            currentTree,
                            StringComparison.OrdinalIgnoreCase)
                        || !await IsIndexAtCommitAsync(
                                repository,
                                runner,
                                expectedRestoreCommit,
                                indexPlan?.Pathspec ?? ".",
                                CancellationToken.None)
                            .ConfigureAwait(false))
                    {
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.RecoveryFailed,
                            new AggregateException(
                                "The tree transition failed and the original tree could not be verified.",
                                transitionException),
                            currentHead);
                    }

                    CheckedOutBranchTip? recoveredTip = await TryGetCheckedOutBranchTipAsync(
                            repository,
                            runner,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (recoveredTip is null || !EqualsBranchTip(recoveredTip, currentHead))
                    {
                        return new TreeTransitionResult(
                            TreeTransitionOutcome.OwnershipLost,
                            transitionException,
                            recoveredTip);
                    }

                    return new TreeTransitionResult(
                        TreeTransitionOutcome.RestoredCurrent,
                        transitionException,
                        currentHead);
                }
                catch (VersionControlConflictedException recoveryException)
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.OwnershipLost,
                        recoveryException,
                        currentHead);
                }
                catch (Exception recoveryException)
                {
                    return new TreeTransitionResult(
                        TreeTransitionOutcome.RecoveryFailed,
                        new AggregateException(
                            "The tree transition failed and its current state could not be restored.",
                            transitionException,
                            recoveryException),
                        currentHead);
                }
            }
        }
        catch (ProjectCheckpointStateChangedException ex)
        {
            return new TreeTransitionResult(
                TreeTransitionOutcome.OwnershipLost,
                ex,
                await TryGetCheckedOutBranchTipAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new TreeTransitionResult(
                mutationStarted
                    ? TreeTransitionOutcome.RecoveryFailed
                    : TreeTransitionOutcome.RestoredCurrent,
                ex,
                currentHead);
        }
        finally
        {
            await RemoveRefUpdateWorktreeBestEffortAsync(
                    repository,
                    runner,
                    refUpdateWorktreePath)
                .ConfigureAwait(false);
        }
    }

    private async Task RemoveRefUpdateWorktreeBestEffortAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string worktreePath)
    {
        Exception? cleanupFailure = null;
        try
        {
            await runner.RunAsync(
                repository,
                ["worktree", "remove", "--force", worktreePath],
                GitCommandOptions.Local,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }

        try
        {
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cleanupFailure = cleanupFailure is null
                ? ex
                : new AggregateException(cleanupFailure, ex);
        }

        if (cleanupFailure is not null)
        {
            LogWarningBestEffort(
                cleanupFailure,
                "Failed to remove a temporary detached Git worktree used for a ref update.");
        }
    }

    private static async Task<string> ResolveGitPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string gitPath,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            ["rev-parse", "--git-path", gitPath],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string path = result.Stdout.TrimEnd('\r', '\n');
        return Path.GetFullPath(
            Path.IsPathFullyQualified(path)
                ? path
                : Path.Combine(repository.RepoRoot, path));
    }

    private static void EnsureTreeTransitionApplied(
        TreeTransitionResult result,
        string message)
    {
        if (result.Outcome == TreeTransitionOutcome.AppliedTarget)
        {
            return;
        }

        if (result.Error is GitOperationException operationException)
        {
            throw operationException;
        }

        throw new InvalidOperationException(
            $"{message} Outcome: {result.Outcome}.",
            result.Error);
    }

    private static Task<GitCommandResult> ResetIndexAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        string pathspec)
    {
        return string.Equals(pathspec, ".", StringComparison.Ordinal)
            ? runner.RunAsync(
                repository,
                ["read-tree", "--reset", commit],
                GitCommandOptions.Local,
                CancellationToken.None)
            : runner.RunAsync(
                repository,
                ["restore", $"--source={commit}", "--staged", "--", pathspec],
                GitCommandOptions.Local,
                CancellationToken.None);
    }

    private static async Task<bool> IsIndexAtCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        string pathspec,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["diff", "--cached", "--quiet", commit, "--", pathspec],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }
    }

    private static string GetCheckpointRefPrefix(RepositoryInfo repository)
    {
        return $"refs/beutl/safety/{GetConfigKeyHash(repository.Pathspec)}/";
    }

    private static string GetPendingRecoveryRefPrefix(RepositoryInfo repository)
    {
        return $"refs/beutl/recovery/{GetConfigKeyHash(repository.Pathspec)}/";
    }

    private static bool EqualsBranchTip(CheckedOutBranchTip left, CheckedOutBranchTip right)
    {
        return string.Equals(left.RefName, right.RefName, StringComparison.Ordinal)
               && string.Equals(left.Commit, right.Commit, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteTemporaryIndex(string path)
    {
        try
        {
            File.Delete(path);
            File.Delete($"{path}.lock");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<WorkspaceStatus> GetStatusCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken,
                extraPathspecs: null)
            .ConfigureAwait(false);
        if (status.Branch is null
            || (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            return status;
        }

        // Counts stay against origin even when the branch tracks a different remote, but the origin
        // branch is whichever one this branch actually tracks: synthesizing it from the local name
        // answers for an unrelated branch whenever the two names differ.
        string? upstream = await TryGetUpstreamRefAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        string originBranchRef =
            upstream is not null && upstream.StartsWith(OriginRefPrefix, StringComparison.Ordinal)
                ? upstream
                : $"{OriginRefPrefix}{status.Branch}";
        if (!await RefExistsAsync(repository, runner, originBranchRef, cancellationToken)
                .ConfigureAwait(false))
        {
            return status with { Ahead = 0, Behind = 0 };
        }

        GitCommandResult counts = await runner.RunAsync(
            repository,
            ["rev-list", "--left-right", "--count", $"HEAD...{originBranchRef}"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        (int ahead, int behind) = ParseAheadBehindCounts(counts.Stdout);
        return status with { Ahead = ahead, Behind = behind };
    }

    private static async Task<string?> TryGetUpstreamRefAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["rev-parse", "--symbolic-full-name", "@{upstream}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string upstream = result.Stdout.Trim();
            return upstream.Length == 0 ? null : upstream;
        }
        catch (GitOperationException)
        {
            // No upstream configured, which git reports as a failure rather than empty output.
            return null;
        }
    }

    // Verified rather than matched: for-each-ref treats its operand as a pattern, so asking for
    // refs/remotes/origin/foo also succeeds when only refs/remotes/origin/foo/bar exists.
    private static async Task<bool> RefExistsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string refName,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["show-ref", "--verify", "--quiet", refName],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    private async Task<WorkspaceStatus> GetSnapshotStatusCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        return await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken,
                CreateSnapshotExcludePathspecs(repository))
            .ConfigureAwait(false);
    }

    private static async Task<WorkspaceStatus> GetStatusCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? extraPathspecs = null)
    {
        string projectPathspec = extraPathspecs is null
            ? repository.Pathspec
            : CreateSnapshotBasePathspec(repository);
        var arguments = new List<string>
        {
            "status",
            "--porcelain=v2",
            "--branch",
            "--untracked-files=all",
            "-z",
            "--",
            projectPathspec,
        };
        if (extraPathspecs is not null)
        {
            arguments.AddRange(extraPathspecs);
        }

        GitCommandResult result = await runner.RunAsync(
            repository,
            arguments,
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                UseLiteralPathspecs: extraPathspecs is null),
            cancellationToken).ConfigureAwait(false);
        WorkspaceStatus status = ParseStatus(result.Stdout);
        if (!repository.IsNestedInForeignRepo || status.HasConflicts)
        {
            return status;
        }

        GitCommandResult unmerged = await runner.RunAsync(
            repository,
            ["ls-files", "--unmerged"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(unmerged.Stdout)
            ? status
            : status with { HasConflicts = true };
    }

    private async Task<IReadOnlyList<CommitInfo>> GetHistoryCoreAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "-c",
                "trailer.separators=:=",
                "log",
                "--no-show-signature",
                "--format=%H%x00%h%x00%an%x00%aI%x00%s%x00%(trailers:key=Beutl-Snapshot,valueonly)%x00",
                "-z",
                $"--skip={skip}",
                "-n",
                take.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--",
                repository.Pathspec,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return ParseHistory(result.Stdout);
    }

    private async Task<IReadOnlyList<FileChange>> GetCommitFilesCoreAsync(
        string sha,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "show",
                "--no-show-signature",
                "--first-parent",
                "--name-status",
                "--format=",
                "-z",
                sha,
                "--",
                repository.Pathspec,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return ParseCommitFiles(result.Stdout);
    }

    private async Task<string> GetDiffCoreAsync(
        string sha,
        string? path,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        string pathspec = path is null
            ? repository.Pathspec
            : ValidateDiffPath(repository, path);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "show",
                "--no-show-signature",
                "--first-parent",
                "--no-color",
                "--format=",
                "--no-ext-diff",
                "--unified=3",
                sha,
                "--",
                pathspec,
            ],
            GitCommandOptions.Local with { MaxStdoutBytes = MaxDiffBytes },
            cancellationToken).ConfigureAwait(false);
        return result.StdoutTruncated
            ? string.Concat(result.Stdout, DiffTruncationMarker)
            : result.Stdout;
    }

    private async Task<IReadOnlyList<BranchInfo>> GetBranchesCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "for-each-ref",
                "--format=%(refname:lstrip=2)%00%(HEAD)%00%(upstream:lstrip=2)",
                "refs/heads",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        return ParseBranches(result.Stdout);
    }

    private async Task<bool> CanCreateBranchCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["check-ref-format", "--branch", name],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string validatedName = RemoveSingleTrailingLineEnding(result.Stdout);
            if (!string.Equals(validatedName, name, StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyList<BranchInfo> branches = await GetBranchesCoreAsync(cancellationToken)
                .ConfigureAwait(false);
            StringComparison branchNameComparison = await UsesCaseInsensitiveFilesRefStorageAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (branches.Any(branch => BranchNamesConflict(
                    branch.Name,
                    name,
                    branchNameComparison)))
            {
                return false;
            }

            return !await HasLooseBranchPathCollisionAsync(
                    repository,
                    runner,
                    name,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    private static string RemoveSingleTrailingLineEnding(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        return value.EndsWith('\n') ? value[..^1] : value;
    }

    private async Task CreateBranchCoreAsync(
        string name,
        string startPoint,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(startPoint, nameof(startPoint));
        if (!await CanCreateBranchCoreAsync(name, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException(
                "The branch must be a valid, unused local branch name.",
                nameof(name));
        }

        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            [.. s_lfsPathFilterOverrides, "switch", "--no-overwrite-ignore", "-c", name, startPoint],
            new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs),
            cancellationToken).ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> GetTrackedReservedPathsAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await GetTrackedReservedPathsCoreAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task UntrackReservedPathsAsync(
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(reservedPaths);
        if (reservedPaths.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunSerializedAsync(
            async () =>
            {
                RepositoryInfo repository = GetRepository();
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                await TryUntrackReservedPathsCoreAsync(
                        repository,
                        runner,
                        reservedPaths,
                        cancellationToken)
                    .ConfigureAwait(false);
                await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    private static bool IsReservedProjectPath(string repositoryRelativePath)
    {
        if (repositoryRelativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string segment in repositoryRelativePath.Split('/'))
        {
            if (string.Equals(segment, ".beutl", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<string>> GetTrackedReservedPathsCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult listed = await runner.RunAsync(
            repository,
            ["ls-files", "-z", "--", CreateSnapshotBasePathspec(repository)],
            new GitCommandOptions(GitCommandExecutionKind.Local) { UseLiteralPathspecs = false },
            cancellationToken).ConfigureAwait(false);
        return listed.Stdout
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsReservedProjectPath)
            .Where(path => !IsRequiredTemporaryRepositoryPath(repository, path))
            .ToArray();
    }

    private bool IsRequiredTemporaryRepositoryPath(
        RepositoryInfo repository,
        string repositoryRelativePath)
    {
        string repositoryPath = Path.Combine(
            repository.RepoRoot,
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, repositoryPath))
        {
            return false;
        }

        foreach (string requiredPath in _requiredTemporaryProjectPaths)
        {
            string requiredProjectPath = Path.Combine(
                repository.ProjectRoot,
                requiredPath.Replace('/', Path.DirectorySeparatorChar));
            if (VersionControlPathComparison.AreSameCanonicalPath(repositoryPath, requiredProjectPath))
            {
                return true;
            }
        }

        return false;
    }

    // .gitignore never untracks what is already tracked, and snapshot status excludes these paths -
    // so a project that is clean to Beutl still leaves the repository dirty for the pull
    // precondition, with no way out from inside the app. Drop them from the index (the files stay on
    // disk) and record that in its own commit: the initialization commit is pathspec-limited with
    // the very excludes that hide these paths, so it would leave the deletion staged forever.
    private async Task TryUntrackReservedPathsCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        string? temporaryIndex = null;
        string? refUpdateWorktreePath = null;
        bool cleanupRefPublished = false;
        try
        {
            string branchRef = await GetAttachedBranchRefCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            CheckedOutBranchTip expectedHead = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(branchRef, expectedHead.RefName, StringComparison.Ordinal))
            {
                throw new ProjectCheckpointStateChangedException();
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            refUpdateWorktreePath = Path.Combine(
                Path.GetTempPath(),
                $"beutl-git-ref-update-{Guid.NewGuid():N}");
            await runner.RunAsync(
                    repository,
                    [
                        "worktree",
                        "add",
                        "--detach",
                        "--no-checkout",
                        refUpdateWorktreePath,
                        expectedHead.Commit,
                    ],
                    GitCommandOptions.Local,
                    cancellationToken)
                .ConfigureAwait(false);
            var refUpdateRepository = new RepositoryInfo(
                refUpdateWorktreePath,
                refUpdateWorktreePath);

            string headPath = await ResolveGitPathAsync(
                    repository,
                    runner,
                    "HEAD",
                    cancellationToken)
                .ConfigureAwait(false);
            using HeadOwnershipLease headLease = HeadOwnershipLease.Acquire(
                headPath,
                expectedHead.RefName,
                ex => LogWarningBestEffort(
                    ex,
                    "Failed to release the protected Git HEAD lock while untracking reserved project paths."));

            temporaryIndex = Path.Combine(
                Path.GetTempPath(),
                $"beutl-git-index-{Guid.NewGuid():N}");
            var indexOptions = new GitCommandOptions(
                GitCommandExecutionKind.Local,
                new Dictionary<string, string?>
                {
                    ["GIT_INDEX_FILE"] = temporaryIndex,
                });
            await runner.RunAsync(
                    repository,
                    ["read-tree", expectedHead.Commit],
                    indexOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            var removeArguments = new List<string>
            {
                "update-index",
                "--force-remove",
                "--",
            };
            removeArguments.AddRange(reservedPaths);
            await runner.RunAsync(
                    repository,
                    removeArguments,
                    indexOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            GitCommandResult desiredTreeResult = await runner.RunAsync(
                    repository,
                    ["write-tree"],
                    indexOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            string desiredTree = desiredTreeResult.Stdout.Trim();
            string currentTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    expectedHead.Commit,
                    cancellationToken)
                .ConfigureAwait(false);

            // If the reserved paths are only staged additions, there is no tree change to publish.
            // Do not mutate the live index: a detached ref movement can race this no-op and the
            // staged-only additions belong to the caller, not to reserved-path hygiene.
            if (string.Equals(desiredTree, currentTree, StringComparison.OrdinalIgnoreCase))
            {
                if (await IsReservedPathCleanupCommitAsync(
                            repository,
                            runner,
                            expectedHead.Commit,
                            reservedPaths,
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    _logger.LogInformation(
                        "The reserved-path cleanup commit is already durable; reconciling only the live index when its ownership can be proven.");
                    await ReconcileReservedPathsInLiveIndexAsync(
                            repository,
                            runner,
                            refUpdateRepository,
                            expectedHead.RefName,
                            expectedHead.Commit,
                            removeArguments,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInformation(
                        "Reserved project paths are staged additions with no cleanup commit; leaving the live index untouched.");
                }

                return;
            }

            string cleanupCommit = await CreateReservedPathCleanupCommitAsync(
                    repository,
                    runner,
                    desiredTree,
                    expectedHead.Commit,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await VerifyUntrackHeadOwnershipAsync(
                    repository,
                    runner,
                    expectedHead)
                .ConfigureAwait(false);
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);

            try
            {
                await runner.RunAsync(
                        refUpdateRepository,
                        [
                            "update-ref",
                            "-m",
                            "beutl: stop tracking reserved project state",
                            expectedHead.RefName,
                            cleanupCommit,
                            expectedHead.Commit,
                        ],
                        GitCommandOptions.Local,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                cleanupRefPublished = true;
            }
            catch (Exception publicationException)
            {
                string? observedTip;
                try
                {
                    observedTip = await TryResolveCommitWithRetryAsync(
                            refUpdateRepository,
                            runner,
                            expectedHead.RefName)
                        .ConfigureAwait(false);
                }
                catch (Exception observationException)
                {
                    if (observationException is GitOperationException
                        {
                            IsRepositoryLockFailure: true,
                        } observationLockException)
                    {
                        throw observationLockException;
                    }

                    throw new AggregateException(
                        "The reserved-path cleanup ref update failed and its result could not be observed after a retry.",
                        publicationException,
                        observationException);
                }

                if (!string.Equals(
                        observedTip,
                        cleanupCommit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (publicationException is GitOperationException
                        {
                            IsRepositoryLockFailure: true,
                        } lockException
                        && string.Equals(
                            observedTip,
                            expectedHead.Commit,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw lockException;
                    }

                    throw new AggregateException(
                        "The reserved-path cleanup ref update was not published because the branch tip changed.",
                        publicationException,
                        new InvalidOperationException(
                            $"Expected branch '{expectedHead.RefName}' at '{expectedHead.Commit}', but observed '{observedTip ?? "<unborn>"}'."));
                }

                cleanupRefPublished = true;
            }

            string? reconciledTip = await TryResolveCommitWithRetryAsync(
                    refUpdateRepository,
                    runner,
                    expectedHead.RefName)
                .ConfigureAwait(false);
            if (!string.Equals(
                    reconciledTip,
                    cleanupCommit,
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Reserved-path cleanup was published, but branch {Branch} moved to {ObservedTip} before the live index could be reconciled; leaving the index untouched.",
                    expectedHead.RefName,
                    reconciledTip ?? "<unborn>");
                return;
            }

            await ReconcileReservedPathsInLiveIndexAsync(
                    repository,
                    runner,
                    refUpdateRepository,
                    expectedHead.RefName,
                    cleanupCommit,
                    removeArguments,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // All tree construction happens in the temporary index. Cancellation before the ref
            // publication therefore leaves both the live index and the branch untouched.
            throw;
        }
        catch (GitOperationException ex) when (ex.IsRepositoryLockFailure)
        {
            // Preserve lock failures for the serialized-operation boundary, which records the
            // recoverable lock instead of silently leaving a stale HEAD.lock/index.lock behind.
            throw;
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                cleanupRefPublished
                    ? "Reserved-path cleanup was committed, but the live index could not be reconciled safely."
                    : "Could not stop tracking reserved project paths; pulls will report the repository dirty until they are untracked manually.");
        }
        finally
        {
            if (refUpdateWorktreePath is not null)
            {
                await RemoveRefUpdateWorktreeBestEffortAsync(
                        repository,
                        runner,
                        refUpdateWorktreePath)
                    .ConfigureAwait(false);
            }

            if (temporaryIndex is not null)
            {
                TryDeleteTemporaryIndex(temporaryIndex);
            }
        }
    }

    private static async Task VerifyUntrackHeadOwnershipAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedHead)
    {
        CheckedOutBranchTip actualHead = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(actualHead, expectedHead))
        {
            throw new ProjectCheckpointStateChangedException();
        }
    }

    private static async Task<bool> IsReservedPathCleanupCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        var historyArguments = new List<string>
        {
            "log",
            "--first-parent",
            "--format=%H",
            "-z",
            "--max-count=128",
            commit,
            "--",
        };
        historyArguments.AddRange(reservedPaths);
        GitCommandResult history = await runner.RunAsync(
            repository,
            historyArguments,
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        foreach (string candidate in history.Stdout.Split(
                     '\0',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (await IsExactReservedPathCleanupCommitAsync(
                        repository,
                        runner,
                        candidate,
                        reservedPaths,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> IsExactReservedPathCleanupCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string commit,
        IReadOnlyList<string> reservedPaths,
        CancellationToken cancellationToken)
    {
        GitCommandResult message = await runner.RunAsync(
            repository,
            ["show", "-s", "--format=%s%n%b", commit],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        if (!message.Stdout.StartsWith(
                "beutl: stop tracking reserved project state\n",
                StringComparison.Ordinal)
            || !message.Stdout.Contains(
                "Beutl-Snapshot: init",
                StringComparison.Ordinal))
        {
            return false;
        }

        GitCommandResult parent = await runner.RunAsync(
            repository,
            ["rev-parse", "--verify", $"{commit}^{{commit}}^"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var diffArguments = new List<string>
        {
            "diff",
            "--name-only",
            "-z",
            parent.Stdout.Trim(),
            commit,
            "--",
        };
        diffArguments.AddRange(reservedPaths);
        GitCommandResult changed = await runner.RunAsync(
            repository,
            diffArguments,
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string[] changedPaths = changed.Stdout.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries);
        return changedPaths.Length > 0
               && changedPaths.All(path => reservedPaths.Contains(path, StringComparer.Ordinal));
    }

    private async Task ReconcileReservedPathsInLiveIndexAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        RepositoryInfo refUpdateRepository,
        string branchRef,
        string expectedTip,
        IReadOnlyList<string> removeArguments,
        CancellationToken cancellationToken)
    {
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string liveIndexPath = await ResolveGitPathAsync(
                repository,
                runner,
                "index",
                cancellationToken)
            .ConfigureAwait(false);
        IndexFileSnapshot liveIndexBefore = await CaptureIndexFileSnapshotAsync(
                liveIndexPath,
                cancellationToken)
            .ConfigureAwait(false);
        IndexFileSnapshot liveIndexAfter = await TransformIndexSnapshotAsync(
                repository,
                runner,
                liveIndexPath,
                liveIndexBefore,
                removeArguments,
                GitCommandOptions.Local,
                "The live Git index changed while reserved paths were being reconciled; it was left untouched.",
                cancellationToken)
            .ConfigureAwait(false);

        bool externalOperationStarted = false;
        try
        {
            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (VersionControlConflictedException)
        {
            externalOperationStarted = true;
        }

        string? finalTip;
        try
        {
            finalTip = await TryResolveCommitWithRetryAsync(
                    refUpdateRepository,
                    runner,
                    branchRef)
                .ConfigureAwait(false);
        }
        catch (Exception observationException)
        {
            try
            {
                await ApplyIndexSnapshotAsync(
                        liveIndexPath,
                        expectedCurrent: liveIndexAfter,
                        replacement: liveIndexBefore,
                        mismatchMessage:
                            "The branch tip became unobservable after reserved-path reconciliation and the live index changed concurrently; the external index state was preserved.")
                    .ConfigureAwait(false);
            }
            catch (IndexRollbackAmbiguousException rollbackException)
            {
                LogWarningBestEffort(
                    rollbackException,
                    "The branch tip became unobservable after reserved-path reconciliation; a concurrent index change was preserved instead of restoring the prior index.");
            }

            LogWarningBestEffort(
                observationException,
                "The branch tip could not be observed after reserved-path index reconciliation; the prior live index was restored when ownership could be proven.");
            if (observationException is GitOperationException
                {
                    IsRepositoryLockFailure: true,
                } observationLockException)
            {
                throw observationLockException;
            }

            return;
        }

        if (!externalOperationStarted
            && string.Equals(finalTip, expectedTip, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await ApplyIndexSnapshotAsync(
                    liveIndexPath,
                    expectedCurrent: liveIndexAfter,
                    replacement: liveIndexBefore,
                    mismatchMessage:
                        "The branch moved after reserved-path reconciliation and the live index changed concurrently; the external index state was preserved.")
                .ConfigureAwait(false);
        }
        catch (IndexRollbackAmbiguousException rollbackException)
        {
            LogWarningBestEffort(
                rollbackException,
                "The branch moved after reserved-path reconciliation; a concurrent index change was preserved instead of restoring the prior index.");
        }
    }

    private Task PrefetchBranchLfsObjectsCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateSwitchBranchName(name);
        return PrefetchLfsObjectsCoreAsync(
            name,
            LfsPrefetchScope.RepositoryWide,
            cancellationToken);
    }

    private Task PrefetchCommitLfsObjectsCoreAsync(
        string sha,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        GitRevisionValidator.ValidateCommitId(sha, nameof(sha));
        return PrefetchLfsObjectsCoreAsync(sha, scope, cancellationToken);
    }

    private async Task PrefetchLfsObjectsCoreAsync(
        string reference,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        (GitAvailability availability, IGitCliRunner? runner) = await GetGitRuntimeCoreAsync(
                cancellationToken)
            .ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || runner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        if (!availability.LfsInstalled)
        {
            if (await TargetContainsLfsPointerAsync(
                    repository,
                    runner,
                    reference,
                    scope,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The target revision contains Git LFS files, but Git LFS is not installed. Install Git LFS before switching or restoring this revision.");
            }

            return;
        }

        LfsPrefetchTarget target = await GetLfsPrefetchTargetAsync(
                repository,
                runner,
                reference,
                scope,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RemoteInfo> remotes = await GetRemotesCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (remotes.Count == 0)
        {
            if (await HasUncachedLfsObjectsAsync(
                    repository,
                    runner,
                    target.Reference,
                    target.PathArguments,
                    cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The transition requires Git LFS objects that are missing or corrupt, and no remote is configured.");
            }

            return;
        }

        try
        {
            await runner.RunAsync(
                repository,
                [
                    .. s_lfsPathFilterOverrides,
                    "-c",
                    "lfs.fetchrecentalways=false",
                    "lfs",
                    "fetch",
                    .. target.PathArguments,
                    remotes[0].Name,
                    target.Reference,
                ],
                GitCommandOptions.Network with
                {
                    MaxStdoutBytes = MaxLfsFetchOutputBytes,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            // The objects may already be cached, so an unreachable endpoint must not turn an
            // otherwise working transition into an error. What it must not do is let the caller
            // close the project and leave the checkout's own smudge filter to download the missing
            // content uncancellably, so the failure is only absorbed when the target needs nothing
            // that is not already in the local object store.
            if (await HasUncachedLfsObjectsAsync(
                    repository,
                    runner,
                    target.Reference,
                    target.PathArguments,
                    cancellationToken)
                    .ConfigureAwait(false))
            {
                throw;
            }

            _logger.LogWarning(
                ex,
                "Could not prefetch Git LFS objects for '{Reference}', but every object it needs is already cached.",
                reference);
        }
    }

    private static async Task<bool> TargetContainsLfsPointerAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string reference,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        var grepArguments = new List<string>
        {
            "grep",
            "-l",
            "-z",
            "--full-name",
            "--no-textconv",
            "--no-ext-grep",
            "-F",
            "-e",
            "version https://git-lfs.github.com/spec/v1",
            "-e",
            "version http://git-media.io/v/2",
            "-e",
            "version https://hawser.github.com/spec/v1",
            reference,
        };
        if (scope == LfsPrefetchScope.ProjectPathspec && repository.Pathspec != ".")
        {
            grepArguments.Add("--");
            grepArguments.Add(repository.Pathspec);
        }
        else if (scope != LfsPrefetchScope.RepositoryWide
                 && scope != LfsPrefetchScope.ProjectPathspec)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        GitCommandResult candidates;
        try
        {
            candidates = await runner.RunAsync(
                    repository,
                    grepArguments,
                    GitCommandOptions.Local with
                    {
                        MaxStdoutBytes = MaxLfsObjectListOutputBytes,
                        CaptureStdoutBytes = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return false;
        }

        if (candidates.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The target revision contains too many possible Git LFS pointer files to inspect safely.");
        }

        string candidateOutput;
        try
        {
            candidateOutput = new UTF8Encoding(false, true).GetString(
                candidates.StdoutBytes
                ?? throw new InvalidOperationException(
                    "Git did not return the Git LFS pointer candidate paths."));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException(
                "Git returned a non-UTF-8 Git LFS pointer candidate path.",
                ex);
        }

        IReadOnlyList<string> records = GitCliRunner.SplitNullSeparated(candidateOutput);
        if (records.Count > MaxLfsPointerCandidates)
        {
            throw new InvalidOperationException(
                "The target revision contains too many possible Git LFS pointer files to inspect safely.");
        }

        string expectedPrefix = reference + ":";
        foreach (string record in records)
        {
            if (!record.StartsWith(expectedPrefix, StringComparison.Ordinal)
                || record.Length == expectedPrefix.Length)
            {
                throw new InvalidOperationException(
                    "Git returned an invalid Git LFS pointer candidate path.");
            }

            string objectExpression = reference + ":" + record[expectedPrefix.Length..];
            GitCommandResult blob = await runner.RunAsync(
                    repository,
                    ["cat-file", "blob", objectExpression],
                    GitCommandOptions.Local with
                    {
                        MaxStdoutBytes = MaxLfsPointerBytes,
                        CaptureStdoutBytes = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (IsLfsPointer(blob.StdoutBytes
                                      ?? throw new InvalidOperationException(
                                          "Git did not return the Git LFS pointer candidate bytes.")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLfsPointer(byte[] contents)
    {
        if (contents.Length == 0 || contents.Length > MaxLfsPointerBytes)
        {
            return false;
        }

        // Git LFS's non-strict decoder operates on bytes and can accept a malformed UTF-8
        // extension name. Replacement decoding retains the ASCII core/extension prefix, so this
        // safety check does not miss a pointer that the unavailable smudge filter would consume.
        string pointer = Encoding.UTF8.GetString(contents).Trim();

        string[] lines = pointer.Split('\n');
        int lineCount = lines.Length;
        if (lineCount > 0 && lines[^1].Length == 0)
        {
            lineCount--;
        }

        for (int i = 0; i < lineCount; i++)
        {
            if (lines[i].EndsWith('\r'))
            {
                lines[i] = lines[i][..^1];
            }
        }

        int index = 0;
        var extensionPriorities = new Dictionary<int, string>();
        if (!SkipLfsPointerExtensions(
                lines,
                lineCount,
                ref index,
                extensionPriorities))
        {
            return false;
        }

        if (index >= lineCount
            || !IsSupportedLfsPointerVersion(lines[index]))
        {
            return false;
        }

        index++;
        if (!SkipLfsPointerExtensions(
                lines,
                lineCount,
                ref index,
                extensionPriorities))
        {
            return false;
        }

        const string OidPrefix = "oid sha256:";
        if (index >= lineCount
            || !lines[index].StartsWith(OidPrefix, StringComparison.Ordinal)
            || lines[index].Length != OidPrefix.Length + 64
            || !IsCanonicalLfsOid(lines[index].AsSpan(OidPrefix.Length)))
        {
            return false;
        }

        index++;
        if (!SkipLfsPointerExtensions(
                lines,
                lineCount,
                ref index,
                extensionPriorities))
        {
            return false;
        }

        const string SizePrefix = "size ";
        if (index >= lineCount
            || !lines[index].StartsWith(SizePrefix, StringComparison.Ordinal)
            || !IsNonNegativeLfsSize(lines[index].AsSpan(SizePrefix.Length)))
        {
            return false;
        }

        index++;
        while (index < lineCount && lines[index].Length == 0)
        {
            index++;
        }

        return index == lineCount;
    }

    private static bool IsSupportedLfsPointerVersion(string line)
    {
        return line is "version https://git-lfs.github.com/spec/v1"
            or "version http://git-media.io/v/2"
            or "version https://hawser.github.com/spec/v1";
    }

    private static bool SkipLfsPointerExtensions(
        string[] lines,
        int lineCount,
        ref int index,
        Dictionary<int, string> priorities)
    {
        while (index < lineCount)
        {
            string line = lines[index];
            if (line.Length == 0)
            {
                index++;
                continue;
            }

            if (!TryParseLfsPointerExtension(line, out int priority, out string key))
            {
                break;
            }

            if (priorities.TryGetValue(priority, out string? existingKey)
                && !string.Equals(existingKey, key, StringComparison.Ordinal))
            {
                return false;
            }

            priorities[priority] = key;
            index++;
        }

        return true;
    }

    private static bool TryParseLfsPointerExtension(
        string line,
        out int priority,
        out string key)
    {
        priority = 0;
        key = string.Empty;
        int separator = line.IndexOf(' ');
        if (separator < 7 || separator >= line.Length - 1)
        {
            return false;
        }

        key = line[..separator];
        if (!key.StartsWith("ext-", StringComparison.Ordinal)
            || key[4] is not (>= '0' and <= '9')
            || key[5] != '-'
            || !IsAsciiWordCharacter(key[6]))
        {
            return false;
        }

        const string OidPrefix = "sha256:";
        ReadOnlySpan<char> value = line.AsSpan(separator + 1);
        if (!value.StartsWith(OidPrefix, StringComparison.Ordinal)
            || value.Length != OidPrefix.Length + 64
            || !IsCanonicalLfsOid(value[OidPrefix.Length..]))
        {
            return false;
        }

        priority = key[4] - '0';
        return true;
    }

    private static bool IsAsciiWordCharacter(char value)
    {
        return value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_';
    }

    private static bool IsNonNegativeLfsSize(ReadOnlySpan<char> value)
    {
        return long.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out long size)
               && size >= 0;
    }

    // Fails safe: anything that stops this from proving the objects are present - an unreadable
    // listing, an unknown storage layout, an unparsable line - counts as uncached, so the caller
    // aborts while it still can instead of closing the project first.
    private async Task<bool> HasUncachedLfsObjectsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string reference,
        IReadOnlyList<string> lfsPathArguments,
        CancellationToken cancellationToken)
    {
        string storage;
        try
        {
            storage = await GetLfsObjectStorageAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            LogWarningBestEffort(
                ex,
                "Could not locate the Git LFS object storage required by a transition target.");
            return true;
        }

        GitCommandResult listed;
        IReadOnlyList<string> requiredObjects;
        try
        {
            listed = await runner.RunAsync(
                repository,
                [
                    .. s_lfsPathFilterOverrides,
                    "lfs",
                    "ls-files",
                    "--long",
                    "--json",
                    .. lfsPathArguments,
                    reference,
                ],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = MaxLfsObjectListOutputBytes,
                },
                cancellationToken).ConfigureAwait(false);

            if (listed.StdoutTruncated
                || !TryParseCanonicalLfsObjectList(listed.Stdout, out requiredObjects))
            {
                return true;
            }
        }
        catch (GitOperationException jsonFailure)
        {
            // --json was added in Git LFS 3.2. Older clients can still prove their cache from the
            // legacy listing: only the OID prefix is a record boundary, so embedded filename
            // newlines are continuations rather than malformed records.
            try
            {
                listed = await runner.RunAsync(
                    repository,
                    [
                        .. s_lfsPathFilterOverrides,
                        "lfs",
                        "ls-files",
                        "--long",
                        .. lfsPathArguments,
                        reference,
                    ],
                    GitCommandOptions.Local with
                    {
                        MaxStdoutBytes = MaxLfsObjectListOutputBytes,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (GitOperationException legacyFailure)
            {
                LogWarningBestEffort(
                    new AggregateException(jsonFailure, legacyFailure),
                    "Could not list the Git LFS objects required by a transition target.");
                return true;
            }

            if (listed.StdoutTruncated
                || !TryParseCanonicalLfsObjectLines(listed.Stdout, out requiredObjects))
            {
                return true;
            }
        }

        var verifiedObjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (string oid in requiredObjects)
        {
            if (verifiedObjects.Add(oid)
                && !await IsCachedLfsObjectValidAsync(
                        storage,
                        oid,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCanonicalLfsObjectList(
        string json,
        out IReadOnlyList<string> oids)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("files", out JsonElement files))
            {
                oids = [];
                return false;
            }

            if (files.ValueKind == JsonValueKind.Null)
            {
                oids = [];
                return true;
            }

            if (files.ValueKind != JsonValueKind.Array)
            {
                oids = [];
                return false;
            }

            var parsed = new List<string>(files.GetArrayLength());
            foreach (JsonElement file in files.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object
                    || !file.TryGetProperty("oid", out JsonElement oidElement)
                    || oidElement.ValueKind != JsonValueKind.String
                    || oidElement.GetString() is not { } oid
                    || oid.Length != 64
                    || !IsCanonicalLfsOid(oid))
                {
                    oids = [];
                    return false;
                }

                parsed.Add(oid);
            }

            oids = parsed;
            return true;
        }
        catch (JsonException)
        {
            oids = [];
            return false;
        }
    }

    private static bool TryParseCanonicalLfsObjectLines(
        string output,
        out IReadOnlyList<string> oids)
    {
        if (output.Length == 0)
        {
            oids = [];
            return true;
        }

        var parsed = new List<string>();
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            const int OidLength = 64;
            if (line.Length >= OidLength + 3
                && line[OidLength] == ' '
                && line[OidLength + 1] is '*' or '-'
                && line[OidLength + 2] == ' '
                && IsCanonicalLfsOid(line.AsSpan(0, OidLength)))
            {
                parsed.Add(line[..OidLength]);
            }
            else if (parsed.Count == 0 && line.Length != 0)
            {
                oids = [];
                return false;
            }
        }

        oids = parsed;
        return parsed.Count > 0;
    }

    private static bool IsCanonicalLfsOid(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> IsCachedLfsObjectValidAsync(
        string storage,
        string oid,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(storage, oid[..2], oid[2..4], oid);
        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            using SHA256 sha256 = SHA256.Create();
            byte[] actual = await sha256.ComputeHashAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            byte[] expected = Convert.FromHexString(oid);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<LfsPrefetchTarget> GetLfsPrefetchTargetAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string reference,
        LfsPrefetchScope scope,
        CancellationToken cancellationToken)
    {
        if (scope == LfsPrefetchScope.RepositoryWide
            || (scope == LfsPrefetchScope.ProjectPathspec && repository.Pathspec == "."))
        {
            return new LfsPrefetchTarget(reference, []);
        }

        if (scope != LfsPrefetchScope.ProjectPathspec)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        // Git LFS parses --include as a comma-separated list of gitignore globs, not as a
        // literal Git pathspec. Use it only when every character is literal in that grammar.
        // For any other legal repository path, resolve the exact subtree with Git's literal
        // pathspec handling and let LFS scan that tree object without a path filter.
        if (repository.Pathspec.All(static character =>
                character is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '/' or '.' or '_' or '-'))
        {
            return new LfsPrefetchTarget(
                reference,
                [
                    $"--include={repository.Pathspec}/**",
                    "--exclude=",
                ]);
        }

        GitCommandResult tree = await runner.RunAsync(
                repository,
                [
                    "ls-tree",
                    "-d",
                    "-z",
                    "--format=%(objectname)",
                    reference,
                    "--",
                    repository.Pathspec,
                ],
                GitCommandOptions.Local with
                {
                    MaxStdoutBytes = 128,
                },
                cancellationToken)
            .ConfigureAwait(false);
        string[] objectIds = GitCliRunner.SplitNullSeparated(tree.Stdout).ToArray();
        if (tree.StdoutTruncated || objectIds.Length != 1)
        {
            throw new InvalidOperationException(
                "The target revision does not contain exactly one project subtree for Git LFS prefetch.");
        }

        string treeId = objectIds[0];
        GitRevisionValidator.ValidateCommitId(treeId, nameof(treeId));
        return new LfsPrefetchTarget(treeId, []);
    }

    private static async Task<string> GetLfsObjectStorageAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        GitCommandResult gitDirectory = await runner.RunAsync(
                repository,
                ["rev-parse", "--git-common-dir"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        string commonDirectory = gitDirectory.Stdout.Trim();
        if (commonDirectory.Length == 0)
        {
            throw new InvalidOperationException("Git returned an empty common directory.");
        }

        string root = Path.GetFullPath(
            Path.IsPathFullyQualified(commonDirectory)
                ? commonDirectory
                : Path.Combine(repository.RepoRoot, commonDirectory));
        GitCommandResult configured = await runner.RunAsync(
            repository,
            ["config", "--get", "--default", "", "lfs.storage"],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string storage = configured.Stdout.Trim();
        if (storage.Length == 0)
        {
            return Path.Combine(root, "lfs", "objects");
        }

        string storageRoot = Path.IsPathFullyQualified(storage)
            ? storage
            : Path.Combine(root, storage);
        return Path.Combine(storageRoot, "objects");
    }

    private async Task SwitchBranchCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateSwitchBranchName(name);
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        IReadOnlyList<BranchInfo> branches = await GetBranchesCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!ContainsLocalBranch(branches, name))
        {
            throw new ArgumentException(
                "The branch must exactly name an existing local branch.",
                nameof(name));
        }

        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            [.. s_lfsPathFilterOverrides, "switch", "--no-overwrite-ignore", name],
            new GitCommandOptions(GitCommandExecutionKind.LocalWithLfs),
            cancellationToken).ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private static void ValidateSwitchBranchName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name[0] == '-')
        {
            throw new ArgumentException(
                "The branch name must not be interpreted as a Git command-line option.",
                nameof(name));
        }
    }

    private static bool ContainsLocalBranch(
        IReadOnlyList<BranchInfo> branches,
        string name)
    {
        return branches.Any(branch =>
            string.Equals(branch.Name, name, StringComparison.Ordinal));
    }

    private static bool BranchNamesConflict(
        string existingName,
        string candidateName,
        StringComparison comparison)
    {
        return string.Equals(existingName, candidateName, comparison)
               || existingName.StartsWith($"{candidateName}/", comparison)
               || candidateName.StartsWith($"{existingName}/", comparison);
    }

    private static async Task<bool> UsesCaseInsensitiveFilesRefStorageAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult storage = await runner.RunAsync(
                repository,
                ["config", "--local", "--get", "extensions.refStorage"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    storage.Stdout.Trim(),
                    "files",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            // The traditional files backend omits extensions.refStorage.
        }

        string headsDirectory = await ResolveGitPathAsync(
                repository,
                runner,
                "refs/heads",
                cancellationToken)
            .ConfigureAwait(false);
        return IsDirectoryStorageCaseInsensitive(headsDirectory);
    }

    private static bool IsDirectoryStorageCaseInsensitive(string directory)
    {
        try
        {
            DirectoryInfo? current = new DirectoryInfo(directory);
            while (current is not null && !current.Exists)
            {
                current = current.Parent;
            }

            while (current?.Parent is not null)
            {
                string aliasName = current.Name.ToUpperInvariant();
                if (string.Equals(aliasName, current.Name, StringComparison.Ordinal))
                {
                    aliasName = current.Name.ToLowerInvariant();
                }

                if (!string.Equals(aliasName, current.Name, StringComparison.Ordinal))
                {
                    string aliasPath = Path.Combine(current.Parent.FullName, aliasName);
                    if (!Directory.Exists(aliasPath))
                    {
                        return false;
                    }

                    bool distinctAliasExists = current.Parent
                        .EnumerateDirectories()
                        .Any(candidate => string.Equals(
                            candidate.Name,
                            aliasName,
                            StringComparison.Ordinal));
                    return !distinctAliasExists;
                }

                current = current.Parent;
            }

            return OperatingSystem.IsWindows();
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException)
        {
            // Conservatively reject case aliases when the files backend cannot be inspected.
            return true;
        }
    }

    private static async Task<bool> HasLooseBranchPathCollisionAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string candidateName,
        CancellationToken cancellationToken)
    {
        string headsDirectory = await ResolveGitPathAsync(
                repository,
                runner,
                "refs/heads",
                cancellationToken)
            .ConfigureAwait(false);
        string candidatePath = Path.Combine(
            headsDirectory,
            candidateName.Replace('/', Path.DirectorySeparatorChar));
        if (Path.Exists(candidatePath))
        {
            return true;
        }

        string? parent = Path.GetDirectoryName(candidatePath);
        while (parent is not null
               && !string.Equals(parent, headsDirectory, StringComparison.Ordinal))
        {
            if (File.Exists(parent))
            {
                return true;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return false;
    }

    private async Task<IReadOnlyList<RemoteInfo>> GetRemotesCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["remote", "get-url", "origin"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string url = result.Stdout.Trim();
            return string.IsNullOrEmpty(url) ? [] : [new RemoteInfo("origin", url)];
        }
        catch (GitOperationException ex) when (IsMissingRemoteFailure(ex))
        {
            return [];
        }
    }

    private async Task SetRemoteCoreAsync(
        string url,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        bool isFirstRemote = (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count == 0;
        if (isFirstRemote)
        {
            await runner.RunAsync(
                repository,
                ["remote", "add", "origin", url],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UpdateLocalConfigAtomicallyAsync(
                repository,
                runner,
                async (stagingPath, updateCancellation) =>
                {
                    await runner.RunAsync(
                        repository,
                        ["config", "--file", stagingPath, "--replace-all", "remote.origin.url", url],
                        GitCommandOptions.Local,
                        updateCancellation).ConfigureAwait(false);
                    await runner.RunAsync(
                        repository,
                        ["config", "--file", stagingPath, "--replace-all", "remote.origin.pushurl", url],
                        GitCommandOptions.Local,
                        updateCancellation).ConfigureAwait(false);
                },
                "remote update",
                cancellationToken).ConfigureAwait(false);
        }

        await TryRaiseLfsQuotaNoticeIfNeededAsync(
            repository,
            runner).ConfigureAwait(false);

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task<RemoteOpResult> PushCoreAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            BranchUpstreamConfiguration? upstream = await GetBranchUpstreamConfigurationAsync(
                    repository,
                    runner,
                    currentTip.RefName,
                    cancellationToken)
                .ConfigureAwait(false);
            string branchName = GetBranchShortName(currentTip.RefName);
            string remoteRef = upstream is not null
                && string.Equals(upstream.RemoteName, "origin", StringComparison.Ordinal)
                && IsValidLocalBranchRef(upstream.RemoteRef)
                ? upstream.RemoteRef
                : $"refs/heads/{branchName}";
            var arguments = new List<string>
            {
                "push",
                "--progress",
            };
            if (upstream is null)
            {
                arguments.Add("-u");
            }

            arguments.Add("origin");
            arguments.Add($"{currentTip.RefName}:{remoteRef}");
            await runner.RunAsync(
                repository,
                arguments,
                GitCommandOptions.Network,
                cancellationToken,
                progress).ConfigureAwait(false);
            await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
            return new RemoteOpResult.Success();
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return MapRemoteFailure(ex);
        }
    }

    private static async Task<BranchUpstreamConfiguration?> GetBranchUpstreamConfigurationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string localBranchRef,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
            repository,
            [
                "for-each-ref",
                "--format=%(refname)%00%(upstream:remotename)%00%(upstream:remoteref)",
                localBranchRef,
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        foreach (string record in result.Stdout
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\0');
            if (fields.Length != 3
                || !string.Equals(fields[0], localBranchRef, StringComparison.Ordinal))
            {
                continue;
            }

            string remoteName = fields[1];
            string remoteRef = fields[2];
            if (remoteName.Length == 0 && remoteRef.Length == 0)
            {
                return null;
            }

            return new BranchUpstreamConfiguration(remoteName, remoteRef);
        }

        throw new GitOperationException(
            128,
            $"The captured local branch '{localBranchRef}' no longer exists.");
    }

    private async Task<PullPreflightResult> PreflightPullCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed before the pull preflight started.");
        }

        bool hasOrigin = (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
        PullFetchTarget fetchTarget = await ResolvePullFetchTargetAsync(
                repository,
                runner,
                hasOrigin,
                expectedCurrent.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await runner.RunAsync(
                repository,
                fetchTarget.Arguments,
                GitCommandOptions.Network,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new PullPreflightResult(
                MapRemoteFailure(ex),
                RequiresTransition: false,
                UpstreamCommit: null);
        }

        string upstreamRef = fetchTarget.UpstreamRef;
        GitCommandResult upstreamResult;
        try
        {
            upstreamResult = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", $"{upstreamRef}^{{commit}}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new PullPreflightResult(
                MapRemoteFailure(ex),
                RequiresTransition: false,
                UpstreamCommit: null);
        }

        string upstreamCommit = upstreamResult.Stdout.Trim();
        PullRelation relation = await GetPullRelationAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);

        currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed while the pull preflight was running.");
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);

        return relation switch
        {
            PullRelation.LocalBehind => new PullPreflightResult(
                new RemoteOpResult.Success(),
                RequiresTransition: true,
                upstreamCommit),
            PullRelation.Equal or PullRelation.LocalAhead => new PullPreflightResult(
                new RemoteOpResult.Success(),
                RequiresTransition: false,
                UpstreamCommit: null),
            _ => new PullPreflightResult(
                new RemoteOpResult.Diverged(),
                RequiresTransition: false,
                UpstreamCommit: null),
        };
    }

    private async Task<FastForwardPullResult> PullFastForwardCoreAsync(
        CheckedOutBranchTip expectedCurrent,
        ProjectCheckpoint? checkpoint,
        string projectFile,
        CancellationToken cancellationToken)
    {
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        ValidateAttachedBranchTip(expectedCurrent, nameof(expectedCurrent));
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureWorktreeMutationAllowed();
        CheckedOutBranchTip currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed before the fast-forward pull started.");
        }

        WorktreeStateFingerprint? checkpointState = null;
        string? checkpointTree = null;
        if (checkpoint is null)
        {
            if (!await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent);
            }
        }
        else
        {
            await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(checkpoint.BaseTip, expectedCurrent))
            {
                throw new InvalidOperationException(
                    "The project checkpoint does not belong to the expected pull tip.");
            }

            checkpointState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    expectedCurrent.Commit,
                    ".",
                    cancellationToken)
                .ConfigureAwait(false);
            checkpointTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    checkpoint.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    checkpointState.Tree,
                    checkpointTree,
                    StringComparison.OrdinalIgnoreCase)
                || !await IsWholeIndexCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent);
            }
        }

        bool hasOrigin = (await GetRemotesCoreAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
        PullFetchTarget fetchTarget = await ResolvePullFetchTargetAsync(
                repository,
                runner,
                hasOrigin,
                expectedCurrent.RefName,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await runner.RunAsync(
                repository,
                fetchTarget.Arguments,
                GitCommandOptions.Network,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new FastForwardPullResult(MapRemoteFailure(ex), expectedCurrent);
        }

        string upstreamRef = fetchTarget.UpstreamRef;
        GitCommandResult upstreamResult;
        try
        {
            upstreamResult = await runner.RunAsync(
                repository,
                ["rev-parse", "--verify", $"{upstreamRef}^{{commit}}"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException ex)
        {
            CaptureRecoverableLock(ex);
            return new FastForwardPullResult(MapRemoteFailure(ex), expectedCurrent);
        }

        string upstreamCommit = upstreamResult.Stdout.Trim();
        PullRelation relation = await GetPullRelationAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        currentTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed while the fast-forward pull was being prepared.");
        }

        if (relation == PullRelation.Diverged)
        {
            return new FastForwardPullResult(new RemoteOpResult.Diverged(), expectedCurrent);
        }

        if (relation == PullRelation.LocalAhead
            || relation == PullRelation.Equal && checkpoint is null)
        {
            return new FastForwardPullResult(new RemoteOpResult.Success(), expectedCurrent);
        }

        string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (ignoredCollision is not null)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                expectedCurrent);
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);

        if (checkpoint is not null)
        {
            return await PullCheckpointedProjectCoreAsync(
                    repository,
                    runner,
                    expectedCurrent,
                    upstreamCommit,
                    checkpoint,
                    checkpointState!,
                    checkpointTree!,
                    projectFile,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        WorktreeStateFingerprint expectedWorktree = await CaptureWorktreeStateAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                ".",
                cancellationToken)
            .ConfigureAwait(false);
        string expectedTree = await ResolveTreeAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        currentTip = await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (!EqualsBranchTip(currentTip, expectedCurrent))
        {
            throw new InvalidOperationException(
                "The checked-out branch changed while the fast-forward pull was being prepared.");
        }

        if (!string.Equals(expectedWorktree.Tree, expectedTree, StringComparison.OrdinalIgnoreCase)
            || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                .ConfigureAwait(false))
        {
            return new FastForwardPullResult(
                new RemoteOpResult.RepositoryDirty(),
                expectedCurrent);
        }

        ignoredCollision = await FindIgnoredIncomingPathAsync(
                repository,
                runner,
                expectedCurrent.Commit,
                upstreamCommit,
                cancellationToken)
            .ConfigureAwait(false);
        if (ignoredCollision is not null)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(
                    $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                expectedCurrent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                CancellationToken.None)
            .ConfigureAwait(false);
        var pulledTip = new CheckedOutBranchTip(expectedCurrent.RefName, upstreamCommit);
        TreeTransitionResult transitionResult = await ApplyTreeTransitionAsync(
            repository,
            runner,
            expectedCurrent,
            pulledTip,
            expectedCurrent.Commit,
            upstreamCommit,
            "pull: fast-forward",
            indexPlan: null,
            CancellationToken.None,
            validatePreparedTarget: () =>
                ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile))
            .ConfigureAwait(false);
        if (transitionResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (transitionResult.Error is GitOperationException operationException)
            {
                CaptureRecoverableLock(operationException);
            }

            RemoteOpResult failure = transitionResult.Outcome switch
            {
                TreeTransitionOutcome.OwnershipLost => new RemoteOpResult.Failed(
                    transitionResult.Error?.Message
                    ?? "The repository changed while the fast-forward pull was being applied."),
                TreeTransitionOutcome.RestoredCurrent when transitionResult.Error is GitOperationException gitException
                    => MapRemoteFailure(gitException),
                _ => new RemoteOpResult.Failed(
                    transitionResult.Error?.Message
                    ?? "The fast-forward pull could not be applied safely."),
            };
            return new FastForwardPullResult(
                failure,
                transitionResult.ActualTip ?? expectedCurrent,
                transitionResult.Outcome switch
                {
                    TreeTransitionOutcome.OwnershipLost => PullTransitionState.OwnershipLost,
                    TreeTransitionOutcome.RecoveryFailed => PullTransitionState.RecoveryFailed,
                    _ => PullTransitionState.Unchanged,
                },
                pulledTip);
        }

        try
        {
            ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                pulledTip,
                PullTransitionState.Applied,
                pulledTip);
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new FastForwardPullResult(
            new RemoteOpResult.Success(),
            pulledTip,
            PullTransitionState.Applied,
            pulledTip);
    }

    private async Task<FastForwardPullResult> PullCheckpointedProjectCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CheckedOutBranchTip expectedCurrent,
        string upstreamCommit,
        ProjectCheckpoint checkpoint,
        WorktreeStateFingerprint expectedCheckpointState,
        string checkpointTree,
        string projectFile,
        CancellationToken cancellationToken)
    {
        string mergedTree = await BuildMergedTreeAsync(
                repository,
                runner,
                checkpoint.BaseTip.Commit,
                upstreamCommit,
                checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult commit = await runner.RunAsync(
            repository,
            [
                "commit-tree",
                mergedTree,
                "-p",
                upstreamCommit,
                "-m",
                PullSafetyCommitMessage,
                "-m",
                "Beutl-Snapshot: safety",
            ],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        var safetyTip = new CheckedOutBranchTip(expectedCurrent.RefName, commit.Stdout.Trim());

        cancellationToken.ThrowIfCancellationRequested();
        PendingPullRecovery recovery = await PersistPendingPullRecoveryCoreAsync(
                checkpoint,
                safetyTip,
                projectFile,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await ValidateCheckpointAsync(repository, runner, checkpoint, CancellationToken.None)
                .ConfigureAwait(false);
            CheckedOutBranchTip ownershipTip = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
            WorktreeStateFingerprint ownershipState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    expectedCurrent.Commit,
                    ".",
                    CancellationToken.None)
                .ConfigureAwait(false);
            string? ignoredCollision = await FindIgnoredIncomingPathAsync(
                    repository,
                    runner,
                    expectedCurrent.Commit,
                    upstreamCommit,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(ownershipTip, expectedCurrent))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.Failed(
                        "The checked-out branch changed while the checkpointed pull was being prepared."),
                    ownershipTip,
                    PullTransitionState.OwnershipLost,
                    safetyTip,
                    recovery);
            }

            if (ownershipState != expectedCheckpointState
                || !string.Equals(
                    ownershipState.Tree,
                    checkpointTree,
                    StringComparison.OrdinalIgnoreCase)
                || !await IsWholeIndexCleanAsync(repository, runner, CancellationToken.None)
                    .ConfigureAwait(false))
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.RepositoryDirty(),
                    expectedCurrent,
                    Recovery: recovery);
            }

            if (ignoredCollision is not null)
            {
                return new FastForwardPullResult(
                    new RemoteOpResult.Failed(
                        $"The pull would overwrite the ignored path '{ignoredCollision}'."),
                    expectedCurrent,
                    Recovery: recovery);
            }

            await EnsureNoExternalRepositoryOperationAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                expectedCurrent,
                PullTransitionState.RecoveryFailed,
                safetyTip,
                recovery);
        }

        TreeTransitionResult transitionResult;
        try
        {
            transitionResult = await ApplyTreeTransitionAsync(
                repository,
                runner,
                expectedCurrent,
                safetyTip,
                checkpoint.Commit,
                safetyTip.Commit,
                "pull: fast-forward with project checkpoint",
                new TreeTransitionIndexPlan(
                    PrepareCommit: checkpoint.Commit,
                    RestoreCommit: expectedCurrent.Commit),
                CancellationToken.None,
                validatePreparedTarget: () =>
                    ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                expectedCurrent,
                PullTransitionState.RecoveryFailed,
                safetyTip,
                recovery);
        }

        if (transitionResult.Outcome != TreeTransitionOutcome.AppliedTarget)
        {
            if (transitionResult.Error is GitOperationException gitException)
            {
                CaptureRecoverableLock(gitException);
            }
            return new FastForwardPullResult(
                transitionResult.Outcome == TreeTransitionOutcome.OwnershipLost
                    ? new RemoteOpResult.Failed(
                        transitionResult.Error?.Message
                        ?? "The repository changed while the checkpointed pull was being applied.")
                    : transitionResult.Error is GitOperationException operationException
                        ? MapRemoteFailure(operationException)
                        : new RemoteOpResult.Failed(
                            transitionResult.Error?.Message
                            ?? "The checkpointed pull could not be applied safely."),
                transitionResult.ActualTip ?? expectedCurrent,
                transitionResult.Outcome switch
                {
                    TreeTransitionOutcome.OwnershipLost => PullTransitionState.OwnershipLost,
                    TreeTransitionOutcome.RecoveryFailed => PullTransitionState.RecoveryFailed,
                    _ => PullTransitionState.Unchanged,
                },
                safetyTip,
                recovery);
        }

        try
        {
            ValidateRecoveryProjectFilePhysicalContainment(repository, projectFile);
        }
        catch (Exception ex)
        {
            return new FastForwardPullResult(
                new RemoteOpResult.Failed(ex.Message),
                safetyTip,
                PullTransitionState.RecoveryFailed,
                safetyTip,
                recovery);
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new FastForwardPullResult(
            new RemoteOpResult.Success(),
            safetyTip,
            PullTransitionState.Applied,
            safetyTip,
            recovery);
    }

    private async Task InitializeCoreAsync(
        InitOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options.TargetRepository);
        string projectRoot = options.TargetRepository.ProjectRoot;

        (GitAvailability availability, IGitCliRunner? nullableRunner)
            = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || nullableRunner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        IGitCliRunner runner = nullableRunner;
        RepositoryInfo? discoveredRepository = Directory.Exists(projectRoot)
            ? await DiscoverRepositoryCoreAsync(
                    projectRoot,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;
        RepositoryInfo repository;
        if (discoveredRepository is { IsNestedInForeignRepo: true })
        {
            if (!MatchesRepositorySelection(discoveredRepository, options.TargetRepository))
            {
                throw new EnclosingRepositoryConsentRequiredException(discoveredRepository);
            }

            repository = discoveredRepository;
        }
        else if (discoveredRepository is not null)
        {
            if (!MatchesRepositorySelection(discoveredRepository, options.TargetRepository))
            {
                throw new InvalidOperationException(
                    "The selected repository does not match the repository containing the project.");
            }

            repository = discoveredRepository;
        }
        else
        {
            if (options.TargetRepository.IsNestedInForeignRepo)
            {
                throw new InvalidOperationException(
                    "The selected existing repository no longer contains the project.");
            }

            repository = options.TargetRepository;
        }

        ValidateProjectSnapshotLayout(repository.ProjectRoot);

        if (Repository is not null
            && !VersionControlPathComparison.AreSameCanonicalPath(Repository.ProjectRoot, projectRoot)
            && !MatchesRepositorySelection(Repository, repository))
        {
            throw new InvalidOperationException(
                "This service is already associated with a different project.");
        }

        GitIdentity? identity = options.Identity;
        if (identity is not null)
        {
            ValidateIdentity(identity);
        }
        else if (Directory.Exists(repository.RepoRoot))
        {
            identity = await GetIdentityCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (identity is null)
        {
            throw new GitIdentityRequiredException();
        }

        EnsureHygienePathsAreSafe(repository);
        if (discoveredRepository is not null)
        {
            await EnsureInitializationPreflightCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            string? ignoredPath = await FindIgnoredRequiredProjectPathBeforeInitAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            ThrowIfRequiredProjectPathIgnored(ignoredPath);
        }

        if (discoveredRepository is null)
        {
            Directory.CreateDirectory(projectRoot);
            Repository = repository;
            await runner.RunAsync(
                repository,
                ["init"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                ["symbolic-ref", "HEAD", "refs/heads/main"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);

            RepositoryInfo? initializedRepository = await DiscoverRepositoryCoreAsync(
                    projectRoot,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            if (initializedRepository is not null
                && !MatchesRepositorySelection(
                    initializedRepository,
                    options.TargetRepository))
            {
                throw new InvalidOperationException(
                    "The initialized repository could not be resolved safely.");
            }

            if (initializedRepository is not null)
            {
                repository = initializedRepository;
                Repository = repository;
            }
        }
        else
        {
            Repository = repository;
        }

        if (options.Identity is not null)
        {
            await SetLocalIdentityCoreAsync(
                    repository,
                    runner,
                    options.Identity,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string ignorePath = Path.Combine(repository.ProjectRoot, ".gitignore");
        string attributesPath = Path.Combine(repository.ProjectRoot, ".gitattributes");
        bool useLfs = options.UseLfsWhenAvailable && availability.LfsInstalled;
        if (useLfs)
        {
            useLfs = await TryInstallLfsLocallyAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await EnsureLinesAsync(
                ignorePath,
                s_gitIgnoreLines,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureAttributesAsync(
                attributesPath,
                useLfs,
                cancellationToken)
            .ConfigureAwait(false);

        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken,
                CreateSnapshotExcludePathspecs(repository))
            .ConfigureAwait(false);
        if (!status.IsClean)
        {
            await RaiseLargeMediaNoticeIfNeededAsync(
                    repository,
                    runner,
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string branchRef = await GetAttachedBranchRefCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string? originalBranchTip = await TryResolveCommitAsync(
                repository,
                runner,
                branchRef,
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotTreeCapture snapshot = await BuildSnapshotTreeForCapturedHeadAsync(
                repository,
                runner,
                branchRef,
                originalBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        string desiredTree = snapshot.Tree;
        string? originalTree = originalBranchTip is null
            ? null
            : await ResolveTreeAsync(
                    repository,
                    runner,
                    originalBranchTip,
                    cancellationToken)
                .ConfigureAwait(false);
        if (originalTree is null
            || !string.Equals(desiredTree, originalTree, StringComparison.OrdinalIgnoreCase))
        {
            using HeadOwnershipLease headLease = await AcquireSnapshotHeadLeaseAsync(
                    repository,
                    runner,
                    branchRef,
                    originalBranchTip,
                    cancellationToken)
                .ConfigureAwait(false);
            SnapshotCommit commit = await CreateSnapshotCommitAsync(
                    repository,
                    runner,
                    desiredTree,
                    originalBranchTip,
                    "beutl: initialize version control",
                    SnapshotKind.Init,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "The initial snapshot did not produce a commit.");
            try
            {
                await PublishSnapshotAndReconcileIndexAsync(
                        repository,
                        runner,
                        branchRef,
                        originalBranchTip,
                        commit.Commit,
                        snapshot,
                        headLease,
                        "beutl: initialize version control",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (ReleaseSnapshotHeadLeaseForPostCommit(headLease))
                {
                    await RunPostCommitHookBestEffortAsync(
                            repository,
                            runner,
                            commit,
                            snapshot.IndexPath)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                TryDeleteTemporaryIndex(commit.MessagePath);
            }
        }

        TryEnsureWatcher();
        await TryRaiseLfsQuotaNoticeIfNeededAsync(
            repository,
            runner).ConfigureAwait(false);
        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitializationPreflightCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathsAreSafe(repository);
        await GetAttachedBranchRefCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        await EnsureRepositoryStatusAndIgnorePreflightCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureRepositoryHygienePreflightCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathsAreSafe(repository);
        await GetCheckedOutBranchTipCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        await EnsureRepositoryStatusAndIgnorePreflightCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureRepositoryStatusAndIgnorePreflightCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfConflicted(status);
        string? ignoredPath = await FindIgnoredRequiredProjectPathAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfRequiredProjectPathIgnored(ignoredPath);
    }

    private async Task EnsureRepositoryHygieneCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        bool useLfs,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathsAreSafe(repository);
        if (useLfs)
        {
            useLfs = await TryInstallLfsLocallyAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await EnsureLinesAsync(
            Path.Combine(repository.ProjectRoot, ".gitignore"),
            s_gitIgnoreLines,
            cancellationToken).ConfigureAwait(false);
        await EnsureAttributesAsync(
            Path.Combine(repository.ProjectRoot, ".gitattributes"),
            useLfs,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryInstallLfsLocallyAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                repository,
                ["lfs", "install", "--local"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GitOperationException ex)
        {
            LogWarningBestEffort(
                ex,
                "Git LFS could not be enabled locally; continuing without Beutl-managed LFS rules.");
            return false;
        }
    }

    private static async Task<RepositoryInfo?> DiscoverRepositoryCoreAsync(
        string projectRoot,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string normalizedProjectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        if (normalizedProjectRoot.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Repository discovery does not support control characters in project paths.",
                nameof(projectRoot));
        }

        var discoveryContext = new RepositoryInfo(normalizedProjectRoot, normalizedProjectRoot);
        try
        {
            GitCommandResult rootResult = await runner.RunAsync(
                discoveryContext,
                ["rev-parse", "--show-toplevel"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            GitCommandResult prefixResult = await runner.RunAsync(
                discoveryContext,
                ["rev-parse", "--show-prefix"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string root = ParseRepositoryDiscoveryPath(
                rootResult.Stdout,
                allowEmpty: false,
                description: "repository root");
            string prefix = ParseRepositoryDiscoveryPath(
                prefixResult.Stdout,
                allowEmpty: true,
                description: "project prefix");
            string repoRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string resolvedProjectRoot = GetDiscoveredProjectRoot(
                repoRoot,
                prefix);
            if (!RepositoryPathComparer.AreEquivalent(
                    resolvedProjectRoot,
                    normalizedProjectRoot))
            {
                throw new InvalidOperationException(
                    "Git repository discovery returned a project root that does not match the requested path.");
            }

            return new RepositoryInfo(repoRoot, resolvedProjectRoot);
        }
        catch (GitOperationException ex) when (IsNotRepositoryFailure(ex))
        {
            return null;
        }
    }

    private static string ParseRepositoryDiscoveryPath(
        string stdout,
        bool allowEmpty,
        string description)
    {
        if (stdout.Length == 0)
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new InvalidOperationException(
                $"Git repository discovery returned an empty {description}.");
        }

        if (!stdout.EndsWith('\n'))
        {
            throw new InvalidOperationException(
                $"Git repository discovery returned an invalid {description} record.");
        }

        string value = stdout[..^1];
        if (value.EndsWith('\r'))
        {
            value = value[..^1];
        }

        if ((!allowEmpty && value.Length == 0) || value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Git repository discovery returned an invalid {description}.");
        }

        return value;
    }

    private static string GetDiscoveredProjectRoot(string repoRoot, string prefix)
    {
        string normalizedPrefix = NormalizeGitPath(prefix);
        if (Path.IsPathFullyQualified(normalizedPrefix)
            || normalizedPrefix
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Contains("..", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Git repository discovery returned an invalid project prefix.");
        }

        string platformPrefix = normalizedPrefix.Replace('/', Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Path.Combine(repoRoot, platformPrefix)));
    }

    private static bool MatchesRepositorySelection(
        RepositoryInfo discovered,
        RepositoryInfo selected)
    {
        return discovered.IsNestedInForeignRepo == selected.IsNestedInForeignRepo
               && RepositoryPathComparer.AreEquivalent(
                   discovered.RepoRoot,
                   selected.RepoRoot)
               && RepositoryPathComparer.AreEquivalent(
                   discovered.ProjectRoot,
                   selected.ProjectRoot);
    }

    private async Task<CommitResult> CommitAllCoreAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken,
        bool presentMissingIdentityNotice = true)
    {
        RepositoryInfo repository = GetRepository();
        await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateProjectSnapshotLayout(repository.ProjectRoot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not OutOfMemoryException)
        {
            // Git can begin a merge after the initial status check and write conflict
            // markers before the project graph is deserialized. Prefer the conflict
            // guidance when that race is observed, but preserve unrelated parse errors.
            try
            {
                await EnsureNotConflictedCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (VersionControlConflictedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception)
            {
            }

            throw;
        }
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        string branchRef = await GetAttachedBranchRefCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string? ignoredPath = await FindIgnoredExistingRequiredProjectPathAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfRequiredProjectPathIgnored(ignoredPath);
        WorkspaceStatus status = await GetSnapshotStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfConflicted(status);
        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);

        string? originalBranchTip = await TryResolveCommitAsync(
                repository,
                runner,
                branchRef,
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotTreeCapture snapshot = await BuildSnapshotTreeForCapturedHeadAsync(
                repository,
                runner,
                branchRef,
                originalBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        string desiredTree = snapshot.Tree;
        if (originalBranchTip is not null)
        {
            string originalTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    originalBranchTip,
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(desiredTree, originalTree, StringComparison.OrdinalIgnoreCase))
            {
                return new CommitResult.NoChanges();
            }
        }
        else if (status.IsClean && _requiredTemporaryProjectPaths.Count == 0)
        {
            return new CommitResult.NoChanges();
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            if (kind != SnapshotKind.Manual)
            {
                if (presentMissingIdentityNotice)
                {
                    await RaiseMissingIdentityNoticeIfNeededAsync(
                            repository,
                            runner,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                return new CommitResult.SkippedNoIdentity();
            }

            throw new GitIdentityRequiredException();
        }

        await RaiseLargeMediaNoticeIfNeededAsync(
            repository,
            runner,
            status,
            cancellationToken).ConfigureAwait(false);

        await EnsureNoExternalRepositoryOperationAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        using HeadOwnershipLease headLease = await AcquireSnapshotHeadLeaseAsync(
                repository,
                runner,
                branchRef,
                originalBranchTip,
                cancellationToken)
            .ConfigureAwait(false);
        SnapshotCommit? commit = await CreateSnapshotCommitAsync(
                repository,
                runner,
                desiredTree,
                originalBranchTip,
                message,
                kind,
                cancellationToken)
            .ConfigureAwait(false);
        if (commit is null)
        {
            return new CommitResult.NoChanges();
        }

        try
        {
            await PublishSnapshotAndReconcileIndexAsync(
                    repository,
                    runner,
                    branchRef,
                    originalBranchTip,
                    commit.Commit,
                    snapshot,
                    headLease,
                    $"beutl: {kind.ToString().ToLowerInvariant()} snapshot",
                    cancellationToken)
                .ConfigureAwait(false);
            if (ReleaseSnapshotHeadLeaseForPostCommit(headLease))
            {
                await RunPostCommitHookBestEffortAsync(
                        repository,
                        runner,
                        commit,
                        snapshot.IndexPath)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            TryDeleteTemporaryIndex(commit.MessagePath);
        }

        await TryQueueStatusChangedCoreAsync().ConfigureAwait(false);
        return new CommitResult.Committed(new CommitRevision.Known(commit.Commit));
    }

    private async Task EnsureNotConflictedCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        WorkspaceStatus status = await GetStatusCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        ThrowIfConflicted(status);
    }

    private static async Task EnsureNoExternalRepositoryOperationAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "rev-parse" };
        foreach (string operationRef in s_repositoryOperationRefs)
        {
            arguments.Add("--git-path");
            arguments.Add(operationRef);
        }

        GitCommandResult result = await runner.RunAsync(
            repository,
            arguments,
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
        string stdout = result.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!stdout.EndsWith('\n'))
        {
            throw new InvalidOperationException(
                "Git returned an invalid repository-operation path list.");
        }

        string[] paths = stdout[..^1].Split('\n');
        if (paths.Length != s_repositoryOperationRefs.Length
            || paths.Any(static path => path.Length == 0 || path.Any(char.IsControl)))
        {
            throw new InvalidOperationException(
                "Git returned an invalid repository-operation path list.");
        }

        foreach (string path in paths)
        {
            string fullPath = Path.GetFullPath(
                Path.IsPathFullyQualified(path)
                    ? path
                    : Path.Combine(repository.RepoRoot, path));
            if (RepositoryOperationPathExists(fullPath))
            {
                throw new VersionControlConflictedException(
                    Strings.VersionControl_ConflictGuidance);
            }
        }
    }

    private static bool RepositoryOperationPathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The Git repository-operation path '{path}' could not be inspected safely.",
                ex);
        }
    }

    private static void ThrowIfConflicted(WorkspaceStatus status)
    {
        if (status.HasConflicts)
        {
            throw new VersionControlConflictedException(Strings.VersionControl_ConflictGuidance);
        }
    }

    private static SnapshotKind ParseSnapshotKind(string trailer)
    {
        string[] values = trailer.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 1)
        {
            return SnapshotKind.Manual;
        }

        return values[0].ToLowerInvariant() switch
        {
            "manual" => SnapshotKind.Manual,
            "save" => SnapshotKind.Save,
            "close" => SnapshotKind.Close,
            "safety" => SnapshotKind.Safety,
            "restore" => SnapshotKind.Restore,
            "recovery" => SnapshotKind.Recovery,
            "init" => SnapshotKind.Init,
            _ => SnapshotKind.Manual,
        };
    }

    private static bool IsNotRepositoryFailure(GitOperationException exception)
    {
        return exception.Stderr.Contains(
                   "not a git repository",
                   StringComparison.OrdinalIgnoreCase)
               || exception.Stderr.Contains(
                   "not in a git directory",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingRemoteFailure(GitOperationException exception)
    {
        return exception.Stderr.Contains(
                   "No such remote",
                   StringComparison.OrdinalIgnoreCase)
               || exception.Stderr.Contains(
                   "does not appear to be a git repository",
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static RemoteOpResult MapRemoteFailure(GitOperationException exception)
    {
        string stderr = exception.Stderr;
        if (ContainsAny(
                stderr,
                "non-fast-forward",
                "not possible to fast-forward",
                "fetch first",
                "divergent branches",
                "[rejected]"))
        {
            return new RemoteOpResult.Diverged();
        }

        if (ContainsAny(
                stderr,
                "authentication failed",
                "permission denied",
                "could not read username",
                "publickey",
                "access denied",
                "authorization failed"))
        {
            return new RemoteOpResult.AuthFailed(Strings.VersionControl_AuthenticationFailed);
        }

        if (ContainsAny(
                stderr,
                "could not resolve host",
                "failed to connect",
                "network is unreachable",
                "connection timed out",
                "connection refused",
                "could not read from remote repository"))
        {
            return new RemoteOpResult.Offline();
        }

        return new RemoteOpResult.Failed(stderr);
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static FileChangeStatus MapNameStatus(char status)
    {
        return status switch
        {
            'A' => FileChangeStatus.Added,
            'D' => FileChangeStatus.Deleted,
            _ => FileChangeStatus.Modified,
        };
    }

    private static string ValidateDiffPath(RepositoryInfo repository, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = NormalizeGitPath(path);
        if (Path.IsPathFullyQualified(path)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Split('/').Any(static segment => segment == ".."))
        {
            throw new ArgumentException("The diff path must be repository-relative.", nameof(path));
        }

        if (repository.Pathspec != "."
            && !string.Equals(normalized, repository.Pathspec, StringComparison.Ordinal)
            && !normalized.StartsWith($"{repository.Pathspec}/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The diff path must be inside the project pathspec.",
                nameof(path));
        }

        return normalized;
    }

    private static string NormalizeGitPath(string path)
        => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private void EnsureWorktreeMutationAllowed()
    {
        if (!_isWorktreeMutationAllowed())
        {
            throw new InvalidOperationException(
                "The project must be closed before changing version-controlled project files.");
        }
    }

    private async Task<IGitCliRunner> GetInstalledRunnerCoreAsync(CancellationToken cancellationToken)
    {
        (GitAvailability availability, IGitCliRunner? runner)
            = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || runner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        return runner;
    }

    private static async Task<GitIdentity?> GetIdentityCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string? name = await TryGetConfigValueAsync(
            repository,
            runner,
            "user.name",
            cancellationToken).ConfigureAwait(false);
        string? email = await TryGetConfigValueAsync(
            repository,
            runner,
            "user.email",
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)
            ? null
            : new GitIdentity(name, email);
    }

    private async Task SetLocalIdentityCoreAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        await UpdateLocalConfigAtomicallyAsync(
            repository,
            runner,
            async (stagingPath, updateCancellation) =>
            {
                await runner.RunAsync(
                    repository,
                    ["config", "--file", stagingPath, "--replace-all", "user.name", identity.Name],
                    GitCommandOptions.Local,
                    updateCancellation).ConfigureAwait(false);
                await runner.RunAsync(
                    repository,
                    ["config", "--file", stagingPath, "--replace-all", "user.email", identity.Email],
                    GitCommandOptions.Local,
                    updateCancellation).ConfigureAwait(false);
            },
            "identity update",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateLocalConfigAtomicallyAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        Func<string, CancellationToken, Task> stageUpdate,
        string operationName,
        CancellationToken cancellationToken)
    {
        string configPath = await ResolveGitPathAsync(
                repository,
                runner,
                "config",
                cancellationToken)
            .ConfigureAwait(false);
        string lockPath = configPath + ".lock";
        string configDirectory = Path.GetDirectoryName(configPath)
                                 ?? throw new InvalidOperationException(
                                     "The local Git configuration has no parent directory.");
        string stagingPath = Path.Combine(
            configDirectory,
            $".beutl-config-{Guid.NewGuid():N}.tmp");
        FileStream lockStream;
        try
        {
            lockStream = new FileStream(
                lockPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException(
                128,
                $"Unable to acquire the local Git configuration lock '{lockPath}': {ex.Message}");
        }

        bool committed = false;
        bool lockPathOwned = true;
        try
        {
            byte[] originalConfig;
            byte[] stagedConfig;
            FileAttributes originalAttributes;
            UnixFileMode? originalUnixMode = null;
            await using (lockStream)
            {
                EnsureLocalConfigPathIsRegular(configPath);
                originalConfig = await File.ReadAllBytesAsync(configPath, cancellationToken)
                    .ConfigureAwait(false);
                originalAttributes = File.GetAttributes(configPath);
                if (!OperatingSystem.IsWindows())
                {
                    originalUnixMode = File.GetUnixFileMode(configPath);
                }

                await using (var stagingStream = new FileStream(
                                 stagingPath,
                                 new FileStreamOptions
                                 {
                                     Mode = FileMode.CreateNew,
                                     Access = FileAccess.Write,
                                     Share = FileShare.None,
                                     Options = FileOptions.Asynchronous,
                                 }))
                {
                    await stagingStream.WriteAsync(originalConfig, cancellationToken)
                        .ConfigureAwait(false);
                    await stagingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                await stageUpdate(stagingPath, cancellationToken).ConfigureAwait(false);

                stagedConfig = await File.ReadAllBytesAsync(stagingPath, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                byte[] currentConfig = await File.ReadAllBytesAsync(
                        configPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!originalConfig.AsSpan().SequenceEqual(currentConfig))
                {
                    throw new InvalidOperationException(
                        $"The local Git configuration changed while the {operationName} was staged.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                await lockStream.WriteAsync(stagedConfig, CancellationToken.None)
                    .ConfigureAwait(false);
                await lockStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                lockStream.Flush(flushToDisk: true);
            }

            File.SetAttributes(lockPath, originalAttributes);
            if (!OperatingSystem.IsWindows() && originalUnixMode is { } unixMode)
            {
                File.SetUnixFileMode(lockPath, unixMode);
            }

            EnsureLocalConfigPathIsRegular(configPath);
            byte[] finalConfig = await File.ReadAllBytesAsync(
                    configPath,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!originalConfig.AsSpan().SequenceEqual(finalConfig))
            {
                throw new InvalidOperationException(
                    $"The local Git configuration changed before the staged {operationName} was committed.");
            }

            if (_beforeFileCommit is not null)
            {
                await _beforeFileCommit(configPath, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string displacedPath = AtomicFileExchange.ReplacePreservingTarget(
                configPath,
                lockPath);
            lockPathOwned = false;
            if (_afterFileExchange is not null)
            {
                await _afterFileExchange(configPath, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await VerifyLocalConfigExchangeAsync(
                    configPath,
                    displacedPath,
                    originalConfig,
                    stagedConfig,
                    operationName)
                .ConfigureAwait(false);
            committed = true;
        }
        finally
        {
            TryDeleteOwnedLocalConfigFile(stagingPath + ".lock");
            TryDeleteOwnedLocalConfigFile(stagingPath);
            if (!committed && lockPathOwned)
            {
                try
                {
                    File.Delete(lockPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogWarningBestEffort(
                        ex,
                        $"Failed to release the local Git configuration lock after a {operationName} failure.");
                }
            }
        }
    }

    private async Task VerifyLocalConfigExchangeAsync(
        string configPath,
        string displacedPath,
        byte[] expectedConfig,
        byte[] stagedConfig,
        string operationName)
    {
        byte[] displacedConfig;
        try
        {
            displacedConfig = await ReadRegularLocalConfigAsync(displacedPath)
                .ConfigureAwait(false);
        }
        catch (Exception inspectionFailure)
        {
            throw new InvalidOperationException(
                $"The local Git configuration changed while the staged {operationName} was committed; the displaced entry was retained at '{displacedPath}'.",
                inspectionFailure);
        }

        if (expectedConfig.AsSpan().SequenceEqual(displacedConfig))
        {
            byte[] committedConfig = await ReadRegularLocalConfigAsync(configPath)
                .ConfigureAwait(false);
            if (!stagedConfig.AsSpan().SequenceEqual(committedConfig))
            {
                if (!TryDeleteOwnedLocalConfigFile(displacedPath))
                {
                    throw new InvalidOperationException(
                        $"The local Git configuration changed while the staged {operationName} was committed; the later edit was preserved and the verified prior contents were retained at '{displacedPath}' because cleanup failed.");
                }

                throw new InvalidOperationException(
                    $"The local Git configuration changed while the staged {operationName} was committed; the later edit was preserved.");
            }

            if (!TryDeleteOwnedLocalConfigFile(displacedPath))
            {
                throw new InvalidOperationException(
                    $"The local Git configuration was updated, but its verified prior contents could not be removed from '{displacedPath}'.");
            }

            return;
        }

        string recoveredCandidatePath;
        try
        {
            recoveredCandidatePath = AtomicFileExchange.ReplacePreservingTarget(
                configPath,
                displacedPath);
        }
        catch (Exception rollbackFailure)
        {
            throw new InvalidOperationException(
                $"The local Git configuration changed while the staged {operationName} was committed; the external edit was retained at '{displacedPath}' because it could not be restored safely.",
                rollbackFailure);
        }

        byte[] recoveredCandidate = await ReadRegularLocalConfigAsync(recoveredCandidatePath)
            .ConfigureAwait(false);
        if (!stagedConfig.AsSpan().SequenceEqual(recoveredCandidate))
        {
            throw new InvalidOperationException(
                $"The local Git configuration changed more than once during the staged {operationName}. The earlier external edit was restored and the later contents were retained at '{recoveredCandidatePath}'.");
        }

        if (!TryDeleteOwnedLocalConfigFile(recoveredCandidatePath))
        {
            throw new InvalidOperationException(
                $"The external local Git configuration edit was restored, but the displaced replacement could not be removed from '{recoveredCandidatePath}'.");
        }

        throw new InvalidOperationException(
            $"The local Git configuration changed while the staged {operationName} was committed; the external edit was preserved.");
    }

    private static async Task<byte[]> ReadRegularLocalConfigAsync(string path)
    {
        EnsureLocalConfigPathIsRegular(path);
        byte[] contents = await File.ReadAllBytesAsync(path, CancellationToken.None)
            .ConfigureAwait(false);
        EnsureLocalConfigPathIsRegular(path);
        return contents;
    }

    private bool TryDeleteOwnedLocalConfigFile(string path)
    {
        try
        {
            return _deleteOwnedLocalConfigFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWarningBestEffort(
                ex,
                "Failed to remove an owned temporary Git configuration file.");
            return false;
        }
    }

    private static bool DeleteOwnedLocalConfigFile(string path)
    {
        File.Delete(path);
        return !Path.Exists(path);
    }

    private static void EnsureLocalConfigPathIsRegular(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists
            || file.LinkTarget is not null
            || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"The local Git configuration path '{path}' is not a regular file.");
        }
    }

    private static void ValidateIdentity(GitIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Email);
    }

    private async Task RaiseLfsQuotaNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RemoteInfo> remotes = await GetRemotesCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        string? remoteUrl = remotes.FirstOrDefault()?.Url;
        if (remoteUrl is null)
        {
            return;
        }

        string acknowledgementKey = LfsQuotaNoticeConfigKeyPrefix
                                    + GetConfigKeyHash(repository.Pathspec);
        if (!await IsLfsActiveAsync(repository, runner, cancellationToken).ConfigureAwait(false)
            || await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!await PresentPolicyNoticeAsync(
                new VersionControlPolicyNotice.LfsRemoteQuota(),
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await SetLocalConfigValueAsync(
            repository,
            runner,
            acknowledgementKey,
            "true",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RaiseLargeMediaNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        WorkspaceStatus status,
        CancellationToken cancellationToken)
    {
        string acknowledgementKey = LargeMediaNoticeConfigKeyPrefix
                                    + GetConfigKeyHash(repository.Pathspec);
        (GitAvailability availability, _) = await GetGitRuntimeCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        long thresholdBytes = Math.Max(
            0L,
            (long)_installationLocator.Config.LargeMediaWarningThresholdMb * 1024 * 1024);
        var candidates = new List<(FileChange Change, string Path)>();
        foreach (FileChange change in status.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? path = GetLargeMediaPath(repository, change.Path, thresholdBytes);
            if (path is not null)
            {
                candidates.Add((change, path));
            }
        }

        HashSet<string> lfsCoveredPaths = availability.LfsInstalled
            ? await GetEffectiveLfsPathsAsync(
                    repository,
                    runner,
                    candidates.Select(static candidate => candidate.Change.Path).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false)
            : [];
        foreach ((FileChange change, string path) in candidates)
        {
            if (lfsCoveredPaths.Contains(change.Path))
            {
                continue;
            }

            if (!TryGetFileLength(path, out long sizeBytes) || sizeBytes <= thresholdBytes)
            {
                continue;
            }

            if (!await PresentPolicyNoticeAsync(
                new VersionControlPolicyNotice.LargeMediaWithoutLfs(
                        NormalizeGitPath(Path.GetRelativePath(repository.ProjectRoot, path)),
                        sizeBytes),
                    cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await SetLocalConfigValueAsync(
                    repository,
                    runner,
                    acknowledgementKey,
                    "true",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to persist the large-media notice acknowledgement.");
            }

            return;
        }
    }

    internal static async Task<HashSet<string>> GetEffectiveLfsPathsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> repoRelativePaths,
        CancellationToken cancellationToken)
    {
        var coveredPaths = new HashSet<string>(StringComparer.Ordinal);
        var chunk = new List<string>();
        int expectedOutputBytes = 0;
        foreach (string path in repoRelativePaths)
        {
            int pathOutputBytes = Encoding.UTF8.GetByteCount(path) + 20;
            if (chunk.Count > 0
                && pathOutputBytes > MaxLfsAttributeOutputBytes - expectedOutputBytes)
            {
                LfsAttributeQueryResult result = await QueryEffectiveLfsPathsAsync(
                        repository,
                        runner,
                        chunk,
                        cancellationToken)
                    .ConfigureAwait(false);
                coveredPaths.UnionWith(result.CoveredPaths);
                if (!result.IsComplete)
                {
                    return coveredPaths;
                }

                chunk.Clear();
                expectedOutputBytes = 0;
            }

            chunk.Add(path);
            expectedOutputBytes = pathOutputBytes > MaxLfsAttributeOutputBytes - expectedOutputBytes
                ? MaxLfsAttributeOutputBytes
                : expectedOutputBytes + pathOutputBytes;
        }

        if (chunk.Count > 0)
        {
            LfsAttributeQueryResult result = await QueryEffectiveLfsPathsAsync(
                    repository,
                    runner,
                    chunk,
                    cancellationToken)
                .ConfigureAwait(false);
            coveredPaths.UnionWith(result.CoveredPaths);
        }

        return coveredPaths;
    }

    private static async Task<LfsAttributeQueryResult> QueryEffectiveLfsPathsAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        IReadOnlyList<string> repoRelativePaths,
        CancellationToken cancellationToken)
    {
        var standardInput = new StringBuilder();
        foreach (string path in repoRelativePaths)
        {
            standardInput.Append(path).Append('\0');
        }

        GitCommandResult result;
        try
        {
            result = await runner.RunAsync(
                repository,
                ["check-attr", "--stdin", "-z", "filter"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    MaxStdoutBytes: MaxLfsAttributeOutputBytes,
                    StandardInput: standardInput.ToString()),
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            return new([], false);
        }
        catch (TimeoutException)
        {
            return new([], false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new([], false);
        }

        if (result.ExitCode != 0 || result.Stderr.Length != 0)
        {
            return new([], false);
        }

        var coveredPaths = new HashSet<string>(StringComparer.Ordinal);
        int position = 0;
        for (int i = 0; i < repoRelativePaths.Count; i++)
        {
            if (!TryReadNullTerminatedField(result.Stdout, ref position, out string path)
                || !TryReadNullTerminatedField(result.Stdout, ref position, out string attribute)
                || !TryReadNullTerminatedField(result.Stdout, ref position, out string value))
            {
                return new(coveredPaths, false);
            }

            if (!string.Equals(path, repoRelativePaths[i], StringComparison.Ordinal)
                || !string.Equals(attribute, "filter", StringComparison.Ordinal))
            {
                return new(coveredPaths, false);
            }

            if (string.Equals(value, "lfs", StringComparison.Ordinal))
            {
                coveredPaths.Add(repoRelativePaths[i]);
            }
        }

        bool isComplete = !result.StdoutTruncated && position == result.Stdout.Length;
        return isComplete
            ? new(coveredPaths, true)
            : new([], false);
    }

    private static bool TryReadNullTerminatedField(
        string value,
        ref int position,
        out string field)
    {
        int end = value.IndexOf('\0', position);
        if (end < 0)
        {
            field = string.Empty;
            return false;
        }

        field = value[position..end];
        position = end + 1;
        return true;
    }

    private async Task RaiseMissingIdentityNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string acknowledgementKey = MissingIdentityNoticeConfigKeyPrefix
                                    + GetConfigKeyHash(repository.Pathspec);
        if (await GetLocalBooleanConfigAsync(
                repository,
                runner,
                acknowledgementKey,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!await PresentPolicyNoticeAsync(
                new VersionControlPolicyNotice.MissingIdentity(),
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await SetLocalConfigValueAsync(
            repository,
            runner,
            acknowledgementKey,
            "true",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PresentPolicyNoticeAsync(
        VersionControlPolicyNotice notice,
        CancellationToken cancellationToken)
    {
        if (_policyNoticeSink is null)
        {
            return false;
        }

        try
        {
            await _policyNoticeSink(notice, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string GetConfigKeyHash(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private async Task<bool> IsLfsActiveAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        (GitAvailability availability, _) = await GetGitRuntimeCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!availability.LfsInstalled)
        {
            return false;
        }

        string prefix = repository.Pathspec == "." ? string.Empty : repository.Pathspec + "/";
        string[] mediaPaths = GetRequiredProjectRelativePaths(repository.ProjectRoot)
            .Where(path => s_mediaExtensions.Contains(Path.GetExtension(path)))
            .Select(path => prefix + path)
            .ToArray();
        HashSet<string> coveredPaths = await GetEffectiveLfsPathsAsync(
                repository,
                runner,
                mediaPaths,
                cancellationToken)
            .ConfigureAwait(false);
        return coveredPaths.Count > 0;
    }

    private static string? GetLargeMediaPath(
        RepositoryInfo repository,
        string repoRelativePath,
        long thresholdBytes)
    {
        string normalizedPath = NormalizeGitPath(repoRelativePath);
        string projectRelativePath;
        if (repository.Pathspec == ".")
        {
            projectRelativePath = normalizedPath;
        }
        else if (normalizedPath.StartsWith($"{repository.Pathspec}/", StringComparison.Ordinal))
        {
            projectRelativePath = normalizedPath[(repository.Pathspec.Length + 1)..];
        }
        else
        {
            return null;
        }

        if (!s_mediaExtensions.Contains(Path.GetExtension(projectRelativePath)))
        {
            return null;
        }

        string path = Path.GetFullPath(Path.Combine(
            repository.ProjectRoot,
            projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!TryGetFileLength(path, out long length) || length <= thresholdBytes)
        {
            return null;
        }

        return path;
    }

    private static bool TryGetFileLength(string path, out long length)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                length = 0;
                return false;
            }

            length = file.Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            length = 0;
            return false;
        }
    }

    private static async Task<bool> GetLocalBooleanConfigAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        CancellationToken cancellationToken)
    {
        string? value = await TryGetConfigValueAsync(
            repository,
            runner,
            key,
            cancellationToken).ConfigureAwait(false);
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static async Task SetLocalConfigValueAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await runner.RunAsync(
            repository,
            ["config", "--local", key, value],
            GitCommandOptions.Local,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> TryGetConfigValueAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                ["config", "--get", key],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            string value = result.Stdout.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }

    private async Task EnsureLinesAsync(
        string path,
        IReadOnlyList<string> requiredLines,
        CancellationToken cancellationToken)
    {
        await UpdateHygieneFileAsync(
            path,
            lines =>
            {
                foreach (string requiredLine in requiredLines)
                {
                    if (!lines.Contains(requiredLine, StringComparer.Ordinal))
                    {
                        lines.Add(requiredLine);
                    }
                }

                return lines;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAttributesAsync(
        string path,
        bool useLfs,
        CancellationToken cancellationToken)
    {
        await UpdateHygieneFileAsync(
            path,
            lines =>
            {
                int? managedBlockIndex = RemoveManagedLfsBlocks(lines);
                foreach (string requiredLine in s_textAttributeLines)
                {
                    if (!lines.Contains(requiredLine, StringComparer.Ordinal))
                    {
                        lines.Add(requiredLine);
                    }
                }

                if (useLfs)
                {
                    int insertionIndex = managedBlockIndex is { } existingIndex
                        ? Math.Min(existingIndex, lines.Count)
                        : 0;
                    lines.InsertRange(
                        insertionIndex,
                        [ManagedLfsBeginMarker, .. s_lfsAttributeLines, ManagedLfsEndMarker]);
                }

                return lines;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasVersionTrackingOptInCoreAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        if (await IsRepositoryHygieneAppliedCoreAsync(repository.ProjectRoot, cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        // The hygiene files can be deleted or checked out away, so the durable record of an
        // earlier opt-in is a snapshot Beutl itself committed for this project. A repository with
        // no readable history has none, and asking again is the safe answer.
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            GitCommandResult result = await runner.RunAsync(
                repository,
                [
                    "log",
                    "--no-show-signature",
                    "--max-count=1",
                    "--format=%H",
                    "--grep=^Beutl-Snapshot: ",
                    "HEAD",
                    "--",
                    repository.Pathspec,
                ],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(result.Stdout);
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> IsRepositoryHygieneAppliedCoreAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        return await HygieneFileContainsLinesAsync(
                   Path.Combine(normalizedRoot, ".gitignore"),
                   s_gitIgnoreLines,
                   cancellationToken)
                   .ConfigureAwait(false)
               && await HygieneFileContainsLinesAsync(
                   Path.Combine(normalizedRoot, ".gitattributes"),
                   s_textAttributeLines,
                   cancellationToken)
                   .ConfigureAwait(false);
    }

    private static async Task<bool> HygieneFileContainsLinesAsync(
        string path,
        IReadOnlyList<string> requiredLines,
        CancellationToken cancellationToken)
    {
        HygieneFileSnapshot snapshot = await ReadHygieneFileSnapshotAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!snapshot.Exists)
        {
            return false;
        }

        List<string> lines = ReadHygieneLines(snapshot.Contents);
        return requiredLines.All(required => lines.Contains(required, StringComparer.Ordinal));
    }

    private static int? RemoveManagedLfsBlocks(List<string> lines)
    {
        int? firstBlockIndex = null;
        int index = 0;
        while (index < lines.Count)
        {
            if (!string.Equals(lines[index], ManagedLfsBeginMarker, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            int end = lines.FindIndex(
                index + 1,
                static line => string.Equals(
                    line,
                    ManagedLfsEndMarker,
                    StringComparison.Ordinal));
            if (end < 0)
            {
                index++;
                continue;
            }

            firstBlockIndex ??= index;
            lines.RemoveRange(index, end - index + 1);
        }

        return firstBlockIndex;
    }

    // The hygiene files are ordinary user-visible files. Portable .NET has no atomic
    // compare-and-delete/replace primitive, so rollback never mutates them after initialization;
    // retaining Beutl's additions is safer than risking deletion or overwrite of an external edit.
    private static async Task<IndexFileSnapshot> CaptureIndexFileSnapshotAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        EnsureIndexPathIsRegular(indexPath);
        try
        {
            if (!File.Exists(indexPath))
            {
                return new IndexFileSnapshot(
                    Exists: false,
                    Contents: [],
                    Attributes: null,
                    UnixMode: null,
                    LastWriteTimeUtc: null);
            }

            byte[] contents = await File.ReadAllBytesAsync(indexPath, cancellationToken)
                .ConfigureAwait(false);
            FileAttributes attributes = File.GetAttributes(indexPath);
            UnixFileMode? unixMode = null;
            if (!OperatingSystem.IsWindows())
            {
                unixMode = File.GetUnixFileMode(indexPath);
            }

            EnsureIndexPathIsRegular(indexPath);
            return new IndexFileSnapshot(
                Exists: true,
                contents,
                attributes,
                unixMode,
                File.GetLastWriteTimeUtc(indexPath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            EnsureIndexPathIsRegular(indexPath);
            return new IndexFileSnapshot(
                Exists: false,
                Contents: [],
                Attributes: null,
                UnixMode: null,
                LastWriteTimeUtc: null);
        }
    }

    private static void EnsureIndexPathIsRegular(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (file.Exists
                && (file.LinkTarget is not null
                    || (file.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                throw new InvalidOperationException(
                    $"Git index path '{path}' must be a regular file.");
            }

            if (Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Git index path '{path}' must be a regular file.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The Git index path '{path}' could not be inspected safely.",
                ex);
        }
    }

    private static bool IndexBytesEqual(
        IndexFileSnapshot left,
        IndexFileSnapshot right)
    {
        return left.Exists == right.Exists
               && left.Contents.AsSpan().SequenceEqual(right.Contents)
               && left.Attributes == right.Attributes
               && left.UnixMode == right.UnixMode;
    }

    private static Task<IndexFileSnapshot> TransformIndexSnapshotAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string indexPath,
        IndexFileSnapshot beforeTransform,
        IReadOnlyList<string> arguments,
        GitCommandOptions options,
        string mismatchMessage,
        CancellationToken cancellationToken)
        => TransformIndexSnapshotAsync(
            repository,
            runner,
            indexPath,
            beforeTransform,
            [arguments],
            options,
            mismatchMessage,
            cancellationToken);

    private static async Task<IndexFileSnapshot> TransformIndexSnapshotAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string indexPath,
        IndexFileSnapshot beforeTransform,
        IReadOnlyList<IReadOnlyList<string>> commands,
        GitCommandOptions options,
        string mismatchMessage,
        CancellationToken cancellationToken)
    {
        string indexDirectory = Path.GetDirectoryName(indexPath)
                                ?? throw new InvalidOperationException(
                                    $"The Git index path '{indexPath}' has no parent directory.");
        string temporaryIndexPath = Path.Combine(
            indexDirectory,
            $".beutl-index-{Guid.NewGuid():N}.tmp");
        try
        {
            if (beforeTransform.Exists)
            {
                await using var stream = new FileStream(
                    temporaryIndexPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous,
                    });
                await stream.WriteAsync(beforeTransform.Contents, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (beforeTransform.LastWriteTimeUtc is { } lastWriteTimeUtc)
            {
                File.SetLastWriteTimeUtc(temporaryIndexPath, lastWriteTimeUtc);
            }

            var environmentOverrides = options.EnvironmentOverrides is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(options.EnvironmentOverrides);
            environmentOverrides["GIT_INDEX_FILE"] = temporaryIndexPath;
            GitCommandOptions temporaryIndexOptions = options with
            {
                EnvironmentOverrides = environmentOverrides,
            };
            foreach (IReadOnlyList<string> arguments in commands)
            {
                await runner.RunAsync(
                        repository,
                        arguments,
                        temporaryIndexOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            IndexFileSnapshot afterAdd = await CaptureIndexFileSnapshotAsync(
                    temporaryIndexPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (beforeTransform.Exists)
            {
                afterAdd = afterAdd with
                {
                    Attributes = beforeTransform.Attributes,
                    UnixMode = beforeTransform.UnixMode,
                };
            }
            await ApplyIndexSnapshotAsync(
                    indexPath,
                    expectedCurrent: beforeTransform,
                    replacement: afterAdd,
                    mismatchMessage: mismatchMessage)
                .ConfigureAwait(false);
            return afterAdd;
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndexPath);
        }
    }

    private static FileStream AcquireIndexLock(string indexPath)
    {
        string lockPath = indexPath + ".lock";
        try
        {
            return new FileStream(
                lockPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException(
                128,
                $"Unable to acquire the worktree Git index lock '{lockPath}': {ex.Message}");
        }
    }

    private static async Task ApplyIndexSnapshotAsync(
        string indexPath,
        IndexFileSnapshot expectedCurrent,
        IndexFileSnapshot replacement,
        string mismatchMessage)
    {
        string lockPath = indexPath + ".lock";
        FileStream lockStream = AcquireIndexLock(indexPath);
        bool lockMoved = false;
        try
        {
            // The lockfile blocks Git's normal index writers for the entire compare/write/rename
            // sequence. A changed byte stream means another writer owns the index and this
            // operation must not overwrite it.
            IndexFileSnapshot current = await CaptureIndexFileSnapshotAsync(
                    indexPath,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!IndexBytesEqual(current, expectedCurrent))
            {
                throw new IndexRollbackAmbiguousException(mismatchMessage);
            }

            await lockStream.WriteAsync(replacement.Contents, CancellationToken.None)
                .ConfigureAwait(false);
            await lockStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            lockStream.Flush(flushToDisk: true);
            lockStream.Dispose();

            if (replacement.Exists)
            {
                if (replacement.Attributes is { } attributes)
                {
                    File.SetAttributes(lockPath, attributes);
                }

                if (!OperatingSystem.IsWindows() && replacement.UnixMode is { } unixMode)
                {
                    File.SetUnixFileMode(lockPath, unixMode);
                }

                if (replacement.LastWriteTimeUtc is { } lastWriteTimeUtc)
                {
                    File.SetLastWriteTimeUtc(lockPath, lastWriteTimeUtc);
                }

                File.Move(lockPath, indexPath, overwrite: true);
            }
            else
            {
                // An absent replacement has no byte stream to rename. Keep the lockfile in place
                // while removing the index so compliant Git writers cannot recreate it between
                // the compare and delete operations.
                File.Delete(indexPath);
                File.Delete(lockPath);
            }

            lockMoved = true;
        }
        catch (IndexRollbackAmbiguousException)
        {
            throw;
        }
        catch (GitOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IndexRollbackAmbiguousException(
                $"The Git index '{indexPath}' could not be changed without risking an overwrite.",
                ex);
        }
        finally
        {
            lockStream.Dispose();

            if (!lockMoved)
            {
                try
                {
                    File.Delete(lockPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Preserve the original ownership/operation failure. The lock is still
                    // visible to compliant Git writers until the caller can recover it.
                }
            }
        }
    }

    private static async Task RestoreFailedIndexSnapshotAsync(
        string indexPath,
        IndexFileSnapshot beforeAdd,
        IndexFileSnapshot afterAdd)
    {
        await ApplyIndexSnapshotAsync(
                indexPath,
                expectedCurrent: afterAdd,
                replacement: beforeAdd,
                mismatchMessage:
                    $"The Git index '{indexPath}' changed after staging; its prior bytes were not restored.")
            .ConfigureAwait(false);
    }

    private async Task UpdateHygieneFileAsync(
        string path,
        Func<List<string>, List<string>> updateLines,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxHygieneWriteAttempts; attempt++)
        {
            HygieneFileSnapshot snapshot = await ReadHygieneFileSnapshotAsync(
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
            List<string> lines = ReadHygieneLines(snapshot.Contents);
            string contents = string.Join('\n', updateLines(lines)) + '\n';
            if (snapshot.Exists
                && string.Equals(snapshot.Contents, contents, StringComparison.Ordinal))
            {
                return;
            }

            string? temporaryPath = await WriteTemporaryHygieneFileAsync(
                    path,
                    contents,
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (_beforeHygieneFileReplace is not null)
                {
                    await _beforeHygieneFileReplace(path, cancellationToken).ConfigureAwait(false);
                }

                HygieneFileSnapshot current = await ReadHygieneFileSnapshotAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (current != snapshot)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                HygieneFileSnapshot finalSnapshot = await ReadHygieneFileSnapshotAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (finalSnapshot != snapshot)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_beforeFileCommit is not null)
                {
                    await _beforeFileCommit(path, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Exists)
                {
                    string candidatePath = temporaryPath;
                    temporaryPath = null;
                    if (await TryCommitExistingHygieneFileAsync(
                                path,
                                candidatePath,
                                snapshot)
                            .ConfigureAwait(false))
                    {
                        return;
                    }

                    continue;
                }

                try
                {
                    File.Move(temporaryPath, path, overwrite: false);
                }
                catch (IOException) when (File.Exists(path))
                {
                    continue;
                }

                return;
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    TryDeleteHygieneTemporaryFile(temporaryPath);
                }
            }
        }

        throw new InvalidOperationException(
            $"Repository hygiene could not update '{path}' because it kept changing.");
    }

    private async Task<bool> TryCommitExistingHygieneFileAsync(
        string path,
        string candidatePath,
        HygieneFileSnapshot expectedSnapshot)
    {
        HygieneFileSnapshot candidateSnapshot = await ReadHygieneFileSnapshotAsync(
                candidatePath,
                CancellationToken.None)
            .ConfigureAwait(false);
        string displacedPath;
        try
        {
            displacedPath = AtomicFileExchange.ReplacePreservingTarget(path, candidatePath);
        }
        catch
        {
            await TryDeleteHygieneFileIfUnchangedAsync(candidatePath, candidateSnapshot)
                .ConfigureAwait(false);
            throw;
        }

        Exception? verificationFailure = null;
        HygieneFileSnapshot? displacedSnapshot = null;
        try
        {
            if (_afterFileExchange is not null)
            {
                await _afterFileExchange(path, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            displacedSnapshot = await ReadHygieneFileSnapshotAsync(
                    displacedPath,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            verificationFailure = ex;
        }

        if (verificationFailure is null && displacedSnapshot == expectedSnapshot)
        {
            if (!_deleteVerifiedHygieneFile(displacedPath))
            {
                throw new InvalidOperationException(
                    $"Repository hygiene was updated, but the verified prior contents could not be removed and were retained at '{displacedPath}'.");
            }

            return true;
        }

        string recoveredCandidatePath;
        try
        {
            recoveredCandidatePath = AtomicFileExchange.ReplacePreservingTarget(
                path,
                displacedPath);
        }
        catch (Exception rollbackFailure)
        {
            Exception failure = verificationFailure is null
                ? rollbackFailure
                : new AggregateException(verificationFailure, rollbackFailure);
            throw new InvalidOperationException(
                $"Repository hygiene changed concurrently; the prior contents were retained at '{displacedPath}' because they could not be restored safely.",
                failure);
        }

        HygieneFileSnapshot recoveredCandidate;
        try
        {
            recoveredCandidate = await ReadHygieneFileSnapshotAsync(
                    recoveredCandidatePath,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception inspectionFailure)
        {
            throw new InvalidOperationException(
                $"Repository hygiene changed concurrently; the displaced replacement was retained at '{recoveredCandidatePath}' because it could not be inspected safely.",
                inspectionFailure);
        }

        if (recoveredCandidate != candidateSnapshot)
        {
            throw new InvalidOperationException(
                $"Repository hygiene changed more than once during recovery. The original file was restored and the later contents were retained at '{recoveredCandidatePath}'.");
        }

        if (!_deleteVerifiedHygieneFile(recoveredCandidatePath))
        {
            throw new InvalidOperationException(
                $"Repository hygiene was restored, but the displaced replacement could not be removed and was retained at '{recoveredCandidatePath}'.");
        }

        if (verificationFailure is not null)
        {
            throw new InvalidOperationException(
                "Repository hygiene changed to an entry that could not be inspected safely; the original entry was restored.",
                verificationFailure);
        }

        return false;
    }

    private static async Task TryDeleteHygieneFileIfUnchangedAsync(
        string path,
        HygieneFileSnapshot expectedSnapshot)
    {
        try
        {
            HygieneFileSnapshot current = await ReadHygieneFileSnapshotAsync(
                    path,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (current == expectedSnapshot)
            {
                TryDeleteHygieneTemporaryFile(path);
            }
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or InvalidOperationException
                                   or NotSupportedException)
        {
            // A path that no longer identifies our exact candidate is retained rather than deleted.
        }
    }

    private static async Task<string> WriteTemporaryHygieneFileAsync(
        string path,
        string contents,
        HygieneFileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)
                           ?? throw new InvalidOperationException(
                               $"The repository hygiene path '{path}' has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous,
                             }))
            await using (var writer = new StreamWriter(
                             stream,
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(contents.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }

            CopyHygieneFileMetadata(temporaryPath, snapshot);
            return temporaryPath;
        }
        catch
        {
            TryDeleteHygieneTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void CopyHygieneFileMetadata(
        string temporaryPath,
        HygieneFileSnapshot snapshot)
    {
        if (snapshot.Attributes is { } attributes)
        {
            File.SetAttributes(temporaryPath, attributes);
        }

        if (!OperatingSystem.IsWindows() && snapshot.UnixMode is { } unixMode)
        {
            File.SetUnixFileMode(temporaryPath, unixMode);
        }
    }

    private static async Task<HygieneFileSnapshot> ReadHygieneFileSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        EnsureHygienePathIsSafe(path);
        try
        {
            if (!File.Exists(path))
            {
                return new HygieneFileSnapshot(
                    Exists: false,
                    Contents: null,
                    Attributes: null,
                    UnixMode: null);
            }

            string contents = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            FileAttributes attributes = File.GetAttributes(path);
            UnixFileMode? unixMode = null;
            if (!OperatingSystem.IsWindows())
            {
                unixMode = File.GetUnixFileMode(path);
            }

            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(
                Exists: true,
                contents,
                attributes,
                unixMode);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            EnsureHygienePathIsSafe(path);
            return new HygieneFileSnapshot(
                Exists: false,
                Contents: null,
                Attributes: null,
                UnixMode: null);
        }
    }

    private static List<string> ReadHygieneLines(string? contents)
    {
        if (string.IsNullOrEmpty(contents))
        {
            return [];
        }

        var lines = new List<string>();
        using var reader = new StringReader(contents);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static void TryDeleteHygieneTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryDeleteVerifiedHygieneFile(string path)
    {
        try
        {
            File.Delete(path);
            return !Path.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record HygieneFileSnapshot(
        bool Exists,
        string? Contents,
        FileAttributes? Attributes,
        UnixFileMode? UnixMode);

    private static void EnsureHygienePathsAreSafe(RepositoryInfo repository)
    {
        EnsureHygienePathIsSafe(Path.Combine(repository.ProjectRoot, ".gitignore"));
        EnsureHygienePathIsSafe(Path.Combine(repository.ProjectRoot, ".gitattributes"));
    }

    private static void EnsureHygienePathIsSafe(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (file.LinkTarget is not null
                || (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0)
                || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Repository hygiene requires '{path}' to be a regular file.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The repository hygiene path '{path}' could not be inspected safely.",
                ex);
        }
    }

    private async Task<string?> FindIgnoredRequiredProjectPathAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        CancellationToken cancellationToken)
    {
        string prefix = repository.Pathspec == "." ? string.Empty : repository.Pathspec + "/";
        var paths = GetRequiredProjectRelativePaths(repository.ProjectRoot)
            .Where(static path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(path => prefix + path)
            .ToList();
        if (repository.Pathspec != ".")
        {
            paths.Add(repository.Pathspec + "/");
        }

        return await FindIgnoredPathAsync(
                repository,
                runner,
                paths,
                environmentOverrides: null,
                includeTrackedFiles: true,
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

        string? ignoredPath = GitCliRunner.SplitNullSeparated(result.Stdout).FirstOrDefault();
        if (ignoredPath is not null)
        {
            return ignoredPath;
        }

        string prefix = repository.Pathspec == "." ? string.Empty : repository.Pathspec + "/";
        return await FindIgnoredPathAsync(
                repository,
                runner,
                GetSerializedProjectRelativePaths(repository.ProjectRoot)
                    .Where(static path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
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
        var result = new List<string>(
            s_ignoredRequiredProjectPathspecSuffixes.Length
            + s_ignoredOptionalProjectPathspecSuffixes.Length);
        foreach (string suffix in s_ignoredRequiredProjectPathspecSuffixes)
        {
            result.Add($":(top,glob){prefix}{suffix}");
        }

        foreach (string suffix in s_ignoredOptionalProjectPathspecSuffixes)
        {
            result.Add($":(top,exclude,glob){prefix}{suffix}");
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

        string probeRoot = Path.Combine(
            Path.GetTempPath(),
            $"beutl-git-ignore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeRoot);
        try
        {
            var probeRepository = new RepositoryInfo(probeRoot, probeRoot);
            await runner.RunAsync(
                probeRepository,
                ["init"],
                GitCommandOptions.Local,
                cancellationToken).ConfigureAwait(false);
            var environmentOverrides = new Dictionary<string, string?>
            {
                ["GIT_DIR"] = Path.Combine(probeRoot, ".git"),
                ["GIT_WORK_TREE"] = repository.ProjectRoot,
            };
            return await FindIgnoredPathAsync(
                    probeRepository,
                    runner,
                    GetRequiredProjectRelativePaths(repository.ProjectRoot)
                        .Where(static path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)),
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

    private IReadOnlyList<string> GetRequiredProjectRelativePaths(string projectRoot)
    {
        IReadOnlySet<string> serializedPaths = GetSerializedProjectRelativePaths(projectRoot);
        var paths = new HashSet<string>(StringComparer.Ordinal)
        {
            ".gitignore",
            ".gitattributes",
            "beutl-required-project.bep",
            "beutl-required-project.scene",
            "beutl-required-project.belm",
        };
        foreach (string extension in s_mediaExtensions)
        {
            paths.Add($"resources/beutl-required-media{extension}");
        }

        if (Directory.Exists(projectRoot))
        {
            foreach (string path in EnumerateRequiredProjectFiles(projectRoot, serializedPaths))
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

        string projectFileDirectory = Path.GetDirectoryName(_projectFile)
                                      ?? throw new InvalidOperationException(
                                          "The project file has no parent directory.");
        string serializationRoot = VersionControlPathComparison.AreSameCanonicalPath(
            projectFileDirectory,
            projectRoot)
            ? projectFileDirectory
            : projectRoot;
        return SerializedProjectGraph.GetRelativePaths(_projectFile, serializationRoot);
    }

    private static async Task<PullFetchTarget> ResolvePullFetchTargetAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        bool hasOrigin,
        string localBranchRef,
        CancellationToken cancellationToken)
    {
        if (!hasOrigin)
        {
            return new PullFetchTarget(["fetch"], "@{upstream}");
        }

        string? configuredUpstream = await TryGetUpstreamRefAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        string branchName = GetBranchShortName(localBranchRef);
        string upstreamRef = $"{OriginRefPrefix}{branchName}";
        if (configuredUpstream is not null
            && configuredUpstream.StartsWith(OriginRefPrefix, StringComparison.Ordinal)
            && configuredUpstream.Length > OriginRefPrefix.Length)
        {
            upstreamRef = configuredUpstream;
            branchName = configuredUpstream[OriginRefPrefix.Length..];
        }

        return new PullFetchTarget(
            [
                "fetch",
                "origin",
                $"+refs/heads/{branchName}:{upstreamRef}",
            ],
            upstreamRef);
    }

    private void ValidateRequiredProjectFileLayout(string projectRoot)
    {
        IReadOnlySet<string> serializedPaths = GetSerializedProjectRelativePaths(projectRoot);
        _requiredTemporaryProjectPaths = serializedPaths
            .Where(static path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.Ordinal);
        _watcher?.UpdateRequiredPaths(serializedPaths);
        foreach (string _ in EnumerateRequiredProjectFiles(projectRoot, serializedPaths))
        {
        }
    }

    private static IEnumerable<string> EnumerateRequiredProjectFiles(
        string projectRoot,
        IReadOnlySet<string> serializedPaths)
    {
        var pending = new Stack<(string Directory, bool IsResourceDirectory)>();
        pending.Push((
            projectRoot,
            IsResourceDirectory: false));
        // Ordinal, not the platform rule: this dedupes directories the walk actually reached, and
        // a case-sensitive volume can hold both Assets/ and assets/ as distinct trees. Folding them
        // together would skip one subtree's symlink and nested-repository validation entirely.
        var visitedDirectories = new HashSet<string>(StringComparer.Ordinal);
        var options = new EnumerationOptions { AttributesToSkip = 0 };
        while (pending.TryPop(out var item))
        {
            string canonicalDirectory = RepositoryPathComparer.ResolveCanonicalPath(item.Directory);
            if (!visitedDirectories.Add(canonicalDirectory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(item.Directory, "*", options))
            {
                string extension = Path.GetExtension(file);
                string relativeFile = NormalizeGitPath(Path.GetRelativePath(projectRoot, file));
                bool isSerializedPath = serializedPaths.Contains(relativeFile);
                if (isSerializedPath
                    || !string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase)
                    && (item.IsResourceDirectory
                        || s_projectFileExtensions.Contains(extension)
                        || s_mediaExtensions.Contains(extension)))
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

            foreach (string child in Directory.EnumerateDirectories(item.Directory, "*", options))
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
                        throw new InvalidOperationException(
                            $"The nested Git repository '{relativeDirectory}' cannot be snapshotted safely.");
                    }

                    pending.Push((
                        child,
                        item.IsResourceDirectory
                        || string.Equals(name, "resources", StringComparison.OrdinalIgnoreCase)));
                }
            }
        }
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

    private static (int Ahead, int Behind) ParseAheadBehindCounts(string output)
    {
        string[] values = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 2
            || !int.TryParse(values[0], out int ahead)
            || !int.TryParse(values[1], out int behind))
        {
            throw new InvalidOperationException("Git returned invalid ahead/behind counts.");
        }

        return (ahead, behind);
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

    private static void ThrowIfRequiredProjectPathIgnored(string? path)
    {
        if (path is not null)
        {
            throw new InvalidOperationException(
                $"The required project path '{path}' is ignored by the repository. "
                + "Update the repository's ignore rules before enabling version control.");
        }
    }

    private static void ValidateRemoteUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "Remote URLs must not embed credentials. Configure a Git credential helper instead.",
                nameof(url));
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return;
        }

        bool isSsh = string.Equals(uri.Scheme, "ssh", StringComparison.OrdinalIgnoreCase);
        bool hasPassword = Uri.UnescapeDataString(uri.UserInfo)
            .Contains(':');
        if (!isSsh || hasPassword)
        {
            throw new ArgumentException(
                "Remote URLs must not embed credentials. Configure a Git credential helper instead.",
                nameof(url));
        }
    }

    private async Task QueueStatusChangedCoreAsync(CancellationToken cancellationToken)
    {
        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        QueueStatusChanged(status);
    }

    private async Task TryQueueStatusChangedCoreAsync()
    {
        try
        {
            await QueueStatusChangedCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to publish version-control status after a durable Git operation.");
        }
    }

    private async Task TryRaiseLfsQuotaNoticeIfNeededAsync(
        RepositoryInfo repository,
        IGitCliRunner runner)
    {
        try
        {
            await RaiseLfsQuotaNoticeIfNeededAsync(
                repository,
                runner,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to publish the Git LFS quota notice after configuring the remote.");
        }
    }

    private void QueueStatusChanged(WorkspaceStatus status)
    {
        _statusNotifications.Enqueue(status);
        if (Interlocked.CompareExchange(ref _statusNotificationDrainScheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _statusNotificationScheduler(DrainStatusNotifications);
        }
        catch
        {
            Volatile.Write(ref _statusNotificationDrainScheduled, 0);
            throw;
        }
    }

    private static void ScheduleStatusNotificationDrain(Action drain)
    {
        ThreadPool.UnsafeQueueUserWorkItem(
            static state => ((Action)state!).Invoke(),
            drain,
            preferLocal: false);
    }

    private void DrainStatusNotifications()
    {
        while (true)
        {
            while (_statusNotifications.TryDequeue(out WorkspaceStatus? status))
            {
                NotifyStatusChanged(status);
            }

            Volatile.Write(ref _statusNotificationDrainScheduled, 0);
            if (_statusNotifications.IsEmpty
                || Interlocked.CompareExchange(ref _statusNotificationDrainScheduled, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private void NotifyStatusChanged(WorkspaceStatus status)
    {
        if (IsDisposed || StatusChanged is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<WorkspaceStatus> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, status);
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to notify a version-control status subscriber.");
            }
        }
    }

    private RepositoryInfo GetRepository()
    {
        return Repository
               ?? throw new InvalidOperationException(
                   "The project is not associated with a Git repository.");
    }

    private void EnsureWatcher()
    {
        lock (_lifetimeSync)
        {
            if (IsDisposed
                || !_createWatcherWhenRepositoryAvailable
                || _watcher is not null
                || Repository is null)
            {
                return;
            }

            _watcher = new RepositoryWatcher(Repository);
            _watcher.UpdateRequiredPaths(_requiredTemporaryProjectPaths);
            _watcher.Changed += OnRepositoryChanged;
        }
    }

    private void TryEnsureWatcher()
    {
        try
        {
            EnsureWatcher();
        }
        catch (Exception ex)
        {
            LogWarningBestEffort(
                ex,
                "Failed to start repository watching after initializing version control.");
        }
    }

    private async Task<(GitAvailability Availability, IGitCliRunner? Runner)> GetGitRuntimeCoreAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            int revision;
            lock (_runtimeSync)
            {
                if (_cachedAvailability is not null)
                {
                    return (_cachedAvailability, _runner);
                }

                revision = _configurationRevision;
            }

            GitAvailability availability = await _installationLocator
                .LocateAsync(cancellationToken)
                .ConfigureAwait(false);
            IGitCliRunner? runner = availability.State == GitAvailabilityState.Installed
                                    && availability.GitPath is not null
                ? _runnerFactory(availability.GitPath)
                : null;

            lock (_runtimeSync)
            {
                if (revision != _configurationRevision)
                {
                    continue;
                }

                _cachedAvailability = availability;
                _runner = runner;
                return (availability, runner);
            }
        }
    }

    private async Task<T> RunSerializedAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RunSerializedAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CaptureRecoverableLock(ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            _ = RefreshStatusFromWatcherAsync();
        }
    }

    private void CaptureRecoverableLock(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);
        while (pending.TryPop(out Exception? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current is GitOperationException gitException
                && TryCaptureRecoverableLock(gitException))
            {
                return;
            }

            if (current is AggregateException aggregate)
            {
                for (int i = aggregate.InnerExceptions.Count - 1; i >= 0; i--)
                {
                    pending.Push(aggregate.InnerExceptions[i]);
                }

                continue;
            }

            if (current.InnerException is { } innerException)
            {
                pending.Push(innerException);
            }
        }
    }

    private bool TryCaptureRecoverableLock(GitOperationException exception)
    {
        IGitCliRunner? runner = _runner;
        RepositoryInfo? repository = Repository;
        if (!exception.IsRepositoryLockFailure
            || repository is null
            || runner is null)
        {
            return false;
        }

        RepositoryLockInfo? lockInfo = runner.GetRecoverableRepositoryLock(repository);
        if (lockInfo is null)
        {
            return false;
        }

        RecoverableLock = lockInfo;
        _lockNotificationScheduler(() => NotifyRecoverableLockAvailable(lockInfo));
        return true;
    }

    private void NotifyRecoverableLockAvailable(RepositoryLockInfo lockInfo)
    {
        if (IsDisposed
            || !ReferenceEquals(RecoverableLock, lockInfo)
            || RecoverableLockAvailable is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<RepositoryLockInfo> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, lockInfo);
            }
            catch (Exception ex)
            {
                LogWarningBestEffort(
                    ex,
                    "Failed to notify a recoverable repository-lock subscriber.");
            }
        }
    }

    private static void ScheduleLockNotification(Action notification)
    {
        ThreadPool.UnsafeQueueUserWorkItem(
            static state => ((Action)state!).Invoke(),
            notification,
            preferLocal: false);
    }

    private void LogWarningBestEffort(Exception exception, string message)
    {
        try
        {
            _logger.LogWarning(exception, message);
        }
        catch
        {
        }
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        lock (_runtimeSync)
        {
            _configurationRevision++;
            _cachedAvailability = null;
            _runner = null;
        }
    }

    private async Task RefreshStatusFromWatcherAsync()
    {
        try
        {
            await RunSerializedAsync(
                    () => QueueStatusChangedCoreAsync(CancellationToken.None),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception) when (IsDisposed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to refresh version-control status after a repository change.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    private bool IsDisposed
        => (ServiceLifetimeState)Volatile.Read(ref _lifetimeState) != ServiceLifetimeState.Active;

    private sealed class Transaction : IProjectVersionControlTransaction
    {
        private readonly GitCliVersionControlService _service;

        public Transaction(GitCliVersionControlService service)
        {
            _service = service;
        }

        public Task<CommitResult> CommitAllAsync(
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => _service.CommitAllCoreAsync(message, kind, cancellationToken);

        public Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(
            CancellationToken cancellationToken)
            => _service.GetCheckedOutBranchTipCoreAsync(cancellationToken);

        public Task<PullPreflightResult> PreflightPullAsync(
            CheckedOutBranchTip expectedCurrent,
            CancellationToken cancellationToken)
            => _service.PreflightPullCoreAsync(expectedCurrent, cancellationToken);

        public Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
            string message,
            CancellationToken cancellationToken)
            => _service.CreateProjectCheckpointCoreAsync(message, cancellationToken);

        public Task<PendingPullRecovery> PersistPendingPullRecoveryAsync(
            ProjectCheckpoint checkpoint,
            CheckedOutBranchTip targetTip,
            string projectFile,
            CancellationToken cancellationToken)
            => _service.PersistPendingPullRecoveryCoreAsync(
                checkpoint,
                targetTip,
                projectFile,
                cancellationToken);

        public Task<IReadOnlyList<PendingPullRecovery>> GetPendingPullRecoveriesAsync(
            CancellationToken cancellationToken)
            => _service.GetPendingPullRecoveriesCoreAsync(cancellationToken);

        public Task<PendingPullRecoveryOutcome> RecoverPendingPullRecoveryAsync(
            PendingPullRecovery recovery,
            CancellationToken cancellationToken)
            => _service.RecoverPendingPullRecoveryCoreAsync(recovery, cancellationToken);

        public Task CompletePendingPullRecoveryAsync(
            PendingPullRecovery recovery,
            CancellationToken cancellationToken)
            => _service.CompletePendingPullRecoveryCoreAsync(recovery, cancellationToken);

        public Task RestoreProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
            => _service.RestoreProjectCheckpointCoreAsync(checkpoint, cancellationToken);

        public Task<CommitResult> CommitProjectTreeAsync(
            CheckedOutBranchTip expectedCurrent,
            string sourceCommit,
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => _service.CommitProjectTreeCoreAsync(
                expectedCurrent,
                sourceCommit,
                message,
                kind,
                cancellationToken);

        public Task<bool> RevisionContainsProjectFileAsync(
            string sha,
            string projectFile,
            CancellationToken cancellationToken)
            => _service.RevisionContainsProjectFileCoreAsync(
                sha,
                projectFile,
                cancellationToken);

        public Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
            CheckedOutBranchTip expectedCurrent,
            CheckedOutBranchTip target,
            CancellationToken cancellationToken)
            => _service.TryRollbackBranchTipCoreAsync(expectedCurrent, target, cancellationToken);

        public Task<bool> DeleteProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
            => _service.DeleteProjectCheckpointCoreAsync(checkpoint, cancellationToken);

        public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
            => _service.GetStatusCoreAsync(cancellationToken);

        public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(
            CancellationToken cancellationToken)
            => _service.GetBranchesCoreAsync(cancellationToken);

        public Task<bool> CanCreateBranchAsync(
            string name,
            CancellationToken cancellationToken)
            => _service.CanCreateBranchCoreAsync(name, cancellationToken);

        public Task CreateBranchAsync(
            string name,
            string startPoint,
            CancellationToken cancellationToken)
            => _service.CreateBranchCoreAsync(name, startPoint, cancellationToken);

        public Task PrefetchBranchLfsObjectsAsync(string name, CancellationToken cancellationToken)
            => _service.PrefetchBranchLfsObjectsCoreAsync(name, cancellationToken);

        public Task PrefetchCommitLfsObjectsAsync(
            string sha,
            LfsPrefetchScope scope,
            CancellationToken cancellationToken)
            => _service.PrefetchCommitLfsObjectsCoreAsync(sha, scope, cancellationToken);

        public Task SwitchBranchAsync(string name, CancellationToken cancellationToken)
            => _service.SwitchBranchCoreAsync(name, cancellationToken);

        public Task<FastForwardPullResult> PullFastForwardAsync(
            CheckedOutBranchTip expectedCurrent,
            ProjectCheckpoint? checkpoint,
            string projectFile,
            CancellationToken cancellationToken)
            => _service.PullFastForwardCoreAsync(
                expectedCurrent,
                checkpoint,
                projectFile,
                cancellationToken);
    }

    private enum ServiceLifetimeState
    {
        Active,
        Retiring,
        Retired,
    }

}

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
