using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed class GitCliVersionControlService : IProjectVersionControlService
{
    internal const int MaxDiffBytes = 1024 * 1024;
    internal const string DiffTruncationMarker = "\n--- Diff truncated at 1 MB ---\n";

    private static readonly string[] s_gitIgnoreLines =
    [
        "**/.beutl/",
        "*.tmp",
    ];

    private static readonly string[] s_textAttributeLines =
    [
        "*.bep text eol=lf",
        "*.scene text eol=lf",
        "*.belm text eol=lf",
        ".gitignore text eol=lf",
        ".gitattributes text eol=lf",
    ];

    private static readonly string[] s_lfsAttributeLines =
    [
        "resources/**/*.mp4 filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.mov filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.mkv filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.avi filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.wmv filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.flv filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.webm filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.wav filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.mp3 filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.flac filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.aac filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.m4a filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.ogg filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.opus filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.wma filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.png filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.jpg filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.jpeg filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.gif filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.bmp filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.webp filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.tiff filter=lfs diff=lfs merge=lfs -text",
        "resources/**/*.tif filter=lfs diff=lfs merge=lfs -text",
    ];

    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<string, IGitCliRunner> _runnerFactory;
    private readonly Func<bool> _isWorktreeMutationAllowed;
    private readonly bool _createWatcherWhenRepositoryAvailable;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _runtimeSync = new();
    private RepositoryWatcher? _watcher;
    private GitAvailability? _cachedAvailability;
    private IGitCliRunner? _runner;
    private int _configurationRevision;
    private bool _disposed;

    public GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository = null)
        : this(
            installationLocator,
            repository,
            repository is null ? null : new RepositoryWatcher(repository.RepoRoot),
            static gitPath => new GitCliRunner(gitPath),
            createWatcherWhenRepositoryAvailable: true,
            isWorktreeMutationAllowed: static () => true)
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        Func<bool> isWorktreeMutationAllowed)
        : this(
            installationLocator,
            repository,
            repository is null ? null : new RepositoryWatcher(repository.RepoRoot),
            static gitPath => new GitCliRunner(gitPath),
            createWatcherWhenRepositoryAvailable: true,
            isWorktreeMutationAllowed: isWorktreeMutationAllowed)
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        RepositoryWatcher? watcher,
        Func<string, IGitCliRunner> runnerFactory)
        : this(
            installationLocator,
            repository,
            watcher,
            runnerFactory,
            createWatcherWhenRepositoryAvailable: false,
            isWorktreeMutationAllowed: static () => true)
    {
    }

    private GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        RepositoryWatcher? watcher,
        Func<string, IGitCliRunner> runnerFactory,
        bool createWatcherWhenRepositoryAvailable,
        Func<bool> isWorktreeMutationAllowed)
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
        _createWatcherWhenRepositoryAvailable = createWatcherWhenRepositoryAvailable;
        if (_watcher is not null)
        {
            _watcher.Changed += OnRepositoryChanged;
        }

        _installationLocator.Config.ConfigurationChanged += OnVersionControlConfigChanged;
    }

    public RepositoryInfo? Repository { get; private set; }

    public event EventHandler<WorkspaceStatus>? StatusChanged;

    public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () => (await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false)).Availability,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        return RunSerializedAsync(
            () => GetDiffCoreAsync(sha, path, cancellationToken),
            cancellationToken);
    }

    public Task RestoreWorktreeFromAsync(
        string sha,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        return RunSerializedAsync(
            () => RestoreWorktreeFromCoreAsync(sha, cancellationToken),
            cancellationToken);
    }

    public Task CreateBranchFromAsync(
        string name,
        string sha,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        return RunSerializedAsync(
            () => CreateBranchFromCoreAsync(name, sha, cancellationToken),
            cancellationToken);
    }

    public Task SwitchBranchAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return RunSerializedAsync(
            () => SwitchBranchCoreAsync(name, cancellationToken),
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
                IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
                await runner.RunAsync(
                    repository,
                    ["config", "--local", "user.name", identity.Name],
                    networkOperation: false,
                    cancellationToken).ConfigureAwait(false);
                await runner.RunAsync(
                    repository,
                    ["config", "--local", "user.email", identity.Email],
                    networkOperation: false,
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_watcher is not null)
        {
            _watcher.Changed -= OnRepositoryChanged;
            _watcher.Dispose();
        }

        _installationLocator.Config.ConfigurationChanged -= OnVersionControlConfigChanged;
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
                string path = GetTailAfterSpaces(record, 9);
                string? oldPath = ++index < records.Count ? records[index] : null;
                changes.Add(new FileChange(path, FileChangeStatus.Renamed, oldPath));
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
                changes.Add(new FileChange(path, FileChangeStatus.Renamed, oldPath));
            }
            else
            {
                string path = fields[index++];
                changes.Add(new FileChange(path, MapNameStatus(statusCode)));
            }
        }

        return changes;
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
        if (statusCode.Contains('R') || statusCode.Contains('C'))
        {
            return FileChangeStatus.Renamed;
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

    private async Task<WorkspaceStatus> GetStatusCoreAsync(CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);

        GitCommandResult result = await runner.RunAsync(
            repository,
            ["status", "--porcelain=v2", "--branch", "-z", "--", repository.Pathspec],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        return ParseStatus(result.Stdout);
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
                "log",
                "--format=%H%x00%h%x00%an%x00%aI%x00%s%x00%(trailers:key=Beutl-Snapshot,valueonly)%x00",
                "-z",
                $"--skip={skip}",
                "-n",
                take.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--",
                repository.Pathspec,
            ],
            networkOperation: false,
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
            ["show", "--name-status", "--format=", "-z", sha, "--", repository.Pathspec],
            networkOperation: false,
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
            ["show", "--format=", "--no-ext-diff", "--unified=3", sha, "--", pathspec],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        return TruncateDiff(result.Stdout);
    }

    private async Task RestoreWorktreeFromCoreAsync(
        string sha,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["restore", $"--source={sha}", "--worktree", "--", repository.Pathspec],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["clean", "-fd", "--", repository.Pathspec],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        // A path present in the source commit but absent from the current index is considered
        // untracked after a worktree-only restore, so clean removes it. Reapply the source tree
        // after cleaning to make restores symmetric without changing the index before the
        // coordinator records the restore commit.
        await runner.RunAsync(
            repository,
            ["restore", $"--source={sha}", "--worktree", "--", repository.Pathspec],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        await QueueStatusChangedCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateBranchFromCoreAsync(
        string name,
        string sha,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["switch", "-c", name, sha],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        await QueueStatusChangedCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SwitchBranchCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            repository,
            ["switch", name],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        await QueueStatusChangedCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync(
        InitOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProjectRoot);
        string projectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.ProjectRoot));
        Directory.CreateDirectory(projectRoot);

        (GitAvailability availability, IGitCliRunner? nullableRunner)
            = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed || nullableRunner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        IGitCliRunner runner = nullableRunner;
        var repository = new RepositoryInfo(projectRoot, projectRoot);
        if (Repository is not null
            && !string.Equals(Repository.ProjectRoot, projectRoot, PathComparison))
        {
            throw new InvalidOperationException(
                "This service is already associated with a different project.");
        }

        if (!Directory.Exists(Path.Combine(projectRoot, ".git")))
        {
            await runner.RunAsync(
                repository,
                ["init", "-b", "main"],
                networkOperation: false,
                cancellationToken).ConfigureAwait(false);
        }

        Repository = repository;
        bool useLfs = options.UseLfsWhenAvailable && availability.LfsInstalled;
        if (useLfs)
        {
            await runner.RunAsync(
                repository,
                ["lfs", "install", "--local"],
                networkOperation: false,
                cancellationToken).ConfigureAwait(false);
        }

        await EnsureLinesAsync(
            Path.Combine(projectRoot, ".gitignore"),
            s_gitIgnoreLines,
            cancellationToken).ConfigureAwait(false);
        await EnsureLinesAsync(
            Path.Combine(projectRoot, ".gitattributes"),
            useLfs ? [.. s_textAttributeLines, .. s_lfsAttributeLines] : s_textAttributeLines,
            cancellationToken).ConfigureAwait(false);

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            EnsureWatcher();
            throw new GitIdentityRequiredException();
        }

        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsClean)
        {
            await runner.RunAsync(
                repository,
                ["add", "-A", "--", repository.Pathspec],
                networkOperation: false,
                cancellationToken).ConfigureAwait(false);
            await runner.RunAsync(
                repository,
                [
                    "commit",
                    "-m",
                    "beutl: initialize version control",
                    "-m",
                    "Beutl-Snapshot: init",
                ],
                networkOperation: false,
                cancellationToken).ConfigureAwait(false);
        }

        EnsureWatcher();
        await QueueStatusChangedCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommitResult> CommitAllCoreAsync(
        string message,
        SnapshotKind kind,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken).ConfigureAwait(false);
        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        if (status.IsClean)
        {
            return new CommitResult.NoChanges();
        }

        GitIdentity? identity = await GetIdentityCoreAsync(repository, runner, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            if (kind != SnapshotKind.Manual)
            {
                return new CommitResult.SkippedNoIdentity();
            }

            throw new GitIdentityRequiredException();
        }

        await runner.RunAsync(
            repository,
            ["add", "-A", "--", repository.Pathspec],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);

        var arguments = new List<string>
        {
            "commit",
            "-m",
            message,
        };
        if (kind != SnapshotKind.Manual)
        {
            arguments.Add("-m");
            arguments.Add($"Beutl-Snapshot: {kind.ToString().ToLowerInvariant()}");
        }

        await runner.RunAsync(
            repository,
            arguments,
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        GitCommandResult revParse = await runner.RunAsync(
            repository,
            ["rev-parse", "HEAD"],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);

        await QueueStatusChangedCoreAsync(cancellationToken).ConfigureAwait(false);
        return new CommitResult.Committed(revParse.Stdout.Trim());
    }

    private static SnapshotKind ParseSnapshotKind(string trailer)
    {
        return trailer.Trim().ToLowerInvariant() switch
        {
            "save" => SnapshotKind.Save,
            "close" => SnapshotKind.Close,
            "safety" => SnapshotKind.Safety,
            "restore" => SnapshotKind.Restore,
            "init" => SnapshotKind.Init,
            _ => SnapshotKind.Manual,
        };
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
        string normalized = path.Replace('\\', '/');
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

    private static string TruncateDiff(string diff)
    {
        if (Encoding.UTF8.GetByteCount(diff) <= MaxDiffBytes)
        {
            return diff;
        }

        int low = 0;
        int high = diff.Length;
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if (Encoding.UTF8.GetByteCount(diff.AsSpan(0, middle)) <= MaxDiffBytes)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (low > 0
            && low < diff.Length
            && char.IsHighSurrogate(diff[low - 1])
            && char.IsLowSurrogate(diff[low]))
        {
            low--;
        }

        return string.Concat(diff.AsSpan(0, low), DiffTruncationMarker);
    }

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
                networkOperation: false,
                cancellationToken).ConfigureAwait(false);
            string value = result.Stdout.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (GitOperationException ex) when (ex.ExitCode == 1)
        {
            return null;
        }
    }

    private static async Task EnsureLinesAsync(
        string path,
        IReadOnlyList<string> requiredLines,
        CancellationToken cancellationToken)
    {
        string? existingContents = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
        var lines = File.Exists(path)
            ? (await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false)).ToList()
            : [];
        foreach (string requiredLine in requiredLines)
        {
            if (!lines.Contains(requiredLine, StringComparer.Ordinal))
            {
                lines.Add(requiredLine);
            }
        }

        string contents = string.Join('\n', lines) + '\n';
        if (!string.Equals(existingContents, contents, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(
                path,
                contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task QueueStatusChangedCoreAsync(CancellationToken cancellationToken)
    {
        WorkspaceStatus status = await GetStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        QueueStatusChanged(status);
    }

    private void QueueStatusChanged(WorkspaceStatus status)
    {
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var payload = ((GitCliVersionControlService Service, WorkspaceStatus Status))state!;
                if (!payload.Service._disposed)
                {
                    payload.Service.StatusChanged?.Invoke(payload.Service, payload.Status);
                }
            },
            (this, status),
            preferLocal: false);
    }

    private RepositoryInfo GetRepository()
    {
        return Repository
               ?? throw new InvalidOperationException(
                   "The project is not associated with a Git repository.");
    }

    private void EnsureWatcher()
    {
        if (!_createWatcherWhenRepositoryAvailable || _watcher is not null || Repository is null)
        {
            return;
        }

        _watcher = new RepositoryWatcher(Repository.RepoRoot);
        _watcher.Changed += OnRepositoryChanged;
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
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        _ = RefreshStatusFromWatcherAsync();
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
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
            WorkspaceStatus status = await GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            QueueStatusChanged(status);
        }
        catch (Exception) when (_disposed)
        {
        }
        catch (GitOperationException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
