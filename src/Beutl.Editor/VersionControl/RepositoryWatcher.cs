namespace Beutl.Editor.VersionControl;

public sealed class RepositoryWatcher : IDisposable
{
    internal static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _sync = new();
    private readonly string _repoRoot;
    private readonly ITimer _debounceTimer;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public RepositoryWatcher(string repoRoot, TimeProvider? timeProvider = null)
        : this(repoRoot, timeProvider ?? TimeProvider.System, startWatching: true)
    {
    }

    internal RepositoryWatcher(string repoRoot, TimeProvider timeProvider, bool startWatching)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repoRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        _debounceTimer = timeProvider.CreateTimer(
            static state => ((RepositoryWatcher)state!).QueueChanged(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        if (startWatching)
        {
            Start();
        }
    }

    public event EventHandler? Changed;

    internal static bool ShouldExcludePath(string repoRoot, string path)
    {
        string relativePath = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
        if (relativePath == ".."
            || relativePath.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath))
        {
            return true;
        }

        if (relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return relativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is ".git" or ".beutl");
    }

    internal void NotifyPathChanged(string path)
    {
        if (ShouldExcludePath(_repoRoot, path))
        {
            return;
        }

        ScheduleChanged();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _debounceTimer.Dispose();
        }
    }

    private void Start()
    {
        if (!Directory.Exists(_repoRoot))
        {
            throw new DirectoryNotFoundException($"Repository directory not found: {_repoRoot}");
        }

        _watcher = new FileSystemWatcher(_repoRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemChanged;
        _watcher.Error += OnWatcherError;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        NotifyPathChanged(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        ScheduleChanged();
    }

    private void ScheduleChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void QueueChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state => ((RepositoryWatcher)state!).RaiseChanged(),
            this,
            preferLocal: false);
    }

    private void RaiseChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
