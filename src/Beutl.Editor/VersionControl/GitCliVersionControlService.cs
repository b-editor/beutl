namespace Beutl.Editor.VersionControl;

public sealed class GitCliVersionControlService : IProjectVersionControlService
{
    private readonly GitInstallationLocator _installationLocator;
    private readonly Func<string, IGitCliRunner> _runnerFactory;
    private readonly RepositoryWatcher? _watcher;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _runtimeSync = new();
    private GitAvailability? _cachedAvailability;
    private IGitCliRunner? _runner;
    private int _configurationRevision;
    private bool _disposed;

    public GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository = null,
        RepositoryWatcher? watcher = null)
        : this(
            installationLocator,
            repository,
            watcher ?? (repository is null ? null : new RepositoryWatcher(repository.RepoRoot)),
            static gitPath => new GitCliRunner(gitPath))
    {
    }

    internal GitCliVersionControlService(
        GitInstallationLocator installationLocator,
        RepositoryInfo? repository,
        RepositoryWatcher? watcher,
        Func<string, IGitCliRunner> runnerFactory)
    {
        _installationLocator = installationLocator
                               ?? throw new ArgumentNullException(nameof(installationLocator));
        Repository = repository;
        _watcher = watcher;
        _runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));
        if (_watcher is not null)
        {
            _watcher.Changed += OnRepositoryChanged;
        }

        _installationLocator.Config.ConfigurationChanged += OnVersionControlConfigChanged;
    }

    public RepositoryInfo? Repository { get; }

    public event EventHandler<WorkspaceStatus>? StatusChanged;

    public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            async () => (await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false)).Availability,
            cancellationToken);
    }

    public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RunSerializedAsync(
            () => GetStatusCoreAsync(cancellationToken),
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
        RepositoryInfo repository = Repository
                                    ?? throw new InvalidOperationException(
                                        "The project is not associated with a Git repository.");
        (GitAvailability availability, IGitCliRunner? runner)
            = await GetGitRuntimeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (availability.State != GitAvailabilityState.Installed
            || availability.GitPath is null
            || runner is null)
        {
            throw new InvalidOperationException("Git is not available.");
        }

        GitCommandResult result = await runner.RunAsync(
            repository,
            ["status", "--porcelain=v2", "--branch", "-z", "--", repository.Pathspec],
            networkOperation: false,
            cancellationToken).ConfigureAwait(false);
        return ParseStatus(result.Stdout);
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
}
