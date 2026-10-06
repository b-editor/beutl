using System.Collections.Concurrent;
using System.Text;
using Avalonia.Threading;
using Beutl.Editor.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

// ディレクトリ変更を監視し、デバウンス付きで変更通知を発行する。
internal sealed partial class DirectoryWatcherService : IDisposable
{
    private static readonly TimeSpan s_debounceInterval = TimeSpan.FromMilliseconds(300);
    private readonly ILogger _logger = Log.CreateLogger<DirectoryWatcherService>();
    private readonly object _stateSync = new();
    private readonly TimeSpan _debounceInterval;
    private readonly Action<Action> _postDelivery;
    private readonly Action<FileSystemWatcher> _startWatcher;
    private readonly ConcurrentDictionary<SpecialDirectoryMembershipKey, bool>
        _templateOrMaterialDirectories = new();
    private readonly ConcurrentDictionary<string, string> _configuredSpecialDirectoryIdentities =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DirectoryIdentity> _directoryIdentities =
        new(StringComparer.Ordinal);
    // Limit consecutive rearm attempts because persistent failures recur immediately.
    private const int MaxErrorRearms = 3;

    private FileSystemWatcher? _watcher;
    private string? _watchedCanonicalPath;
    private string? _watchedRequestedPath;
    private CancellationTokenSource? _debounceCts;
    private readonly HashSet<string> _pendingEntryPaths = new(StringComparer.Ordinal);
    private bool _pendingContentChange;
    private int _errorRearmCount;
    private string? _failingCanonicalPath;
    private bool _disposed;
    private long _stateGeneration;

    public DirectoryWatcherService()
        : this(
            s_debounceInterval,
            callback => Dispatcher.UIThread.Post(callback, DispatcherPriority.Background))
    {
    }

    internal DirectoryWatcherService(
        TimeSpan debounceInterval,
        Action<Action> postDelivery,
        Action<FileSystemWatcher>? startWatcher = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(debounceInterval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(postDelivery);

        _debounceInterval = debounceInterval;
        _postDelivery = postDelivery;
        _startWatcher = startWatcher
                        ?? (static watcher => watcher.EnableRaisingEvents = true);
    }

    // ファイルシステムに変更があったときに発火する。UIスレッドで呼び出される。
    public event Action? Changed;

    // These writes may add/remove visible entries, but do not require reloading unchanged items.
    public event Action<IReadOnlyCollection<string>>? EntriesChanged;

    internal bool IsWatching
    {
        get
        {
            lock (_stateSync)
            {
                return _watcher is not null;
            }
        }
    }

    // 指定パスの監視を開始する。前回の監視は自動的に停止される。
    public void Watch(string? path) => Watch(path, isErrorRearm: false);

    private void Watch(string? path, bool isErrorRearm)
    {
        FileSystemWatcher? currentWatcher;
        string? watchedCanonicalPath;
        lock (_stateSync)
        {
            if (_disposed)
                return;

            currentWatcher = _watcher;
            watchedCanonicalPath = _watchedCanonicalPath;
        }

        bool pathResolved = TryResolveWatchPath(path, out string? canonicalPath);

        // Only changing folders clears a failure budget.
        if (!isErrorRearm)
            ResetErrorBudget(pathResolved, canonicalPath);

        // Recursive watchers consume one inotify descriptor per subdirectory.
        if (currentWatcher is not null
            && pathResolved
            && canonicalPath is not null
            && Directory.Exists(canonicalPath)
            && string.Equals(
                watchedCanonicalPath,
                canonicalPath,
                StringComparison.Ordinal)
            && TryKeepCurrentWatcher(currentWatcher, canonicalPath, path!))
        {
            return;
        }

        if (!TryDetachWatcher(
                out long watchGeneration,
                out CancellationTokenSource? previousDebounce,
                out FileSystemWatcher? previousWatcher))
        {
            return;
        }

        ReleaseDetached(previousDebounce, previousWatcher);

        if (!pathResolved || canonicalPath is null || !Directory.Exists(canonicalPath))
            return;

        FileSystemWatcher? nextWatcher = null;
        try
        {
            nextWatcher = CreateWatcher(canonicalPath);
            if (TryInstallWatcher(nextWatcher, watchGeneration, canonicalPath, path!))
                nextWatcher = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", canonicalPath);
        }
        finally
        {
            nextWatcher?.Dispose();
        }
    }

    private void ResetErrorBudget(bool pathResolved, string? canonicalPath)
    {
        lock (_stateSync)
        {
            if (!pathResolved
                || !string.Equals(
                    _failingCanonicalPath,
                    canonicalPath,
                    StringComparison.Ordinal))
            {
                _errorRearmCount = 0;
                _failingCanonicalPath = null;
            }
        }
    }

    // The watcher already covers the folder; only the spelling the items use changes.
    private bool TryKeepCurrentWatcher(FileSystemWatcher currentWatcher, string canonicalPath, string path)
    {
        lock (_stateSync)
        {
            if (!_disposed
                && ReferenceEquals(_watcher, currentWatcher)
                && string.Equals(
                    _watchedCanonicalPath,
                    canonicalPath,
                    StringComparison.Ordinal))
            {
                _watchedRequestedPath = Path.GetFullPath(path);
                return true;
            }
        }

        return false;
    }

    // Takes the watcher and its pending delivery out of the published state; the returned generation
    // lets the replacement tell whether another Watch or Dispose came in after this one.
    private bool TryDetachWatcher(
        out long generation,
        out CancellationTokenSource? debounce,
        out FileSystemWatcher? watcher)
    {
        lock (_stateSync)
        {
            if (_disposed)
            {
                generation = 0;
                debounce = null;
                watcher = null;
                return false;
            }

            generation = ++_stateGeneration;
            debounce = _debounceCts;
            _debounceCts = null;
            ClearPendingChanges();
            watcher = _watcher;
            _watcher = null;
            _watchedCanonicalPath = null;
            _watchedRequestedPath = null;
            return true;
        }
    }

    private void ReleaseDetached(CancellationTokenSource? debounce, FileSystemWatcher? watcher)
    {
        CancelAndDispose(debounce);
        watcher?.Dispose();
        _templateOrMaterialDirectories.Clear();
        _directoryIdentities.Clear();
    }

    private FileSystemWatcher CreateWatcher(string canonicalPath)
    {
        var watcher = new FileSystemWatcher(canonicalPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
        };

        watcher.Created += OnFileSystemEvent;
        watcher.Deleted += OnFileSystemEvent;
        watcher.Renamed += OnFileSystemEvent;
        watcher.Changed += OnFileSystemEvent;
        watcher.Error += OnWatcherError;
        return watcher;
    }

    // False when a later Watch or Dispose superseded this one; the caller then still owns the watcher.
    private bool TryInstallWatcher(FileSystemWatcher watcher, long generation, string canonicalPath, string path)
    {
        lock (_stateSync)
        {
            if (_disposed || _stateGeneration != generation)
                return false;

            _watcher = watcher;
            _watchedCanonicalPath = canonicalPath;
            _watchedRequestedPath = Path.GetFullPath(path);
            try
            {
                _startWatcher(watcher);
            }
            catch
            {
                _watcher = null;
                _watchedCanonicalPath = null;
                _watchedRequestedPath = null;
                throw;
            }

            return true;
        }
    }

    // Rebuild after Error; otherwise the current folder stays unwatched.
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (!IsCurrentWatcher(sender))
            return;

        _logger.LogWarning(e.GetException(), "FileSystemWatcher stopped; rebuilding it");

        Dispatcher.UIThread.Post(() =>
        {
            lock (_stateSync)
            {
                if (!IsCurrentWatcher(sender))
                    return;

                if (TryRearmAfterError())
                {
                    // Resync changes missed while the watcher was down.
                    Changed?.Invoke();
                }
            }
        });
    }

    internal bool TryRearmAfterError()
    {
        string? requestedPath;
        string? canonicalPath;
        FileSystemWatcher? watcher;
        CancellationTokenSource? debounce;
        lock (_stateSync)
        {
            if (_disposed)
                return false;

            watcher = _watcher;
            requestedPath = _watchedRequestedPath ?? watcher?.Path;
            canonicalPath = _watchedCanonicalPath ?? watcher?.Path;
            _watcher = null;
            _watchedCanonicalPath = null;
            _watchedRequestedPath = null;
            debounce = _debounceCts;
            _debounceCts = null;
            ClearPendingChanges();
            _stateGeneration++;
        }

        CancelAndDispose(debounce);
        watcher?.Dispose();
        _templateOrMaterialDirectories.Clear();
        _configuredSpecialDirectoryIdentities.Clear();
        _directoryIdentities.Clear();

        if (requestedPath is null || canonicalPath is null)
        {
            return false;
        }

        lock (_stateSync)
        {
            _failingCanonicalPath ??= canonicalPath;
        }

        // Watch swallows a construction failure, and with no watcher no further Error can arrive to
        // spend what is left of the budget.
        while (true)
        {
            lock (_stateSync)
            {
                if (_disposed || _errorRearmCount >= MaxErrorRearms)
                {
                    break;
                }

                _errorRearmCount++;
            }

            Watch(requestedPath, isErrorRearm: true);
            if (IsWatching)
                return true;
        }

        _logger.LogWarning(
            "FileSystemWatcher for {Path} could not be rebuilt in {Count} attempts; leaving it off until the folder changes",
            requestedPath,
            GetErrorRearmCount());
        return false;
    }

    private int GetErrorRearmCount()
    {
        lock (_stateSync)
        {
            return _errorRearmCount;
        }
    }

    private bool IsCurrentWatcher(object sender)
    {
        lock (_stateSync)
        {
            return !_disposed && ReferenceEquals(_watcher, sender);
        }
    }

    private bool TryResolveWatchPath(string? path, out string? canonicalPath)
    {
        canonicalPath = null;
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        try
        {
            canonicalPath = FilePathComparison.ResolveCanonicalPath(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve watched directory path {Path}",
                path);
            return false;
        }
    }

    internal void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (IsCurrentWatcher(sender))
        {
            // Windows also reports a parent directory's last-write change for child writes.
            // Child name/write events already describe the update; this duplicate would turn
            // an atomic editor save into a full refresh that replaces the existing tree items.
            if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath))
                return;

            if (e.Name is not null)
                NotifyPathChanged(e.FullPath, sender);
            if (e is RenamedEventArgs { OldName: not null } renamed)
                NotifyPathChanged(renamed.OldFullPath, sender);
        }
    }

    internal void NotifyPathChanged(string path) => NotifyPathChanged(path, sourceWatcher: null);

    private void NotifyPathChanged(string path, object? sourceWatcher)
    {
        lock (_stateSync)
        {
            if (_disposed || sourceWatcher is not null && !ReferenceEquals(_watcher, sourceWatcher))
                return;
        }

        bool entriesOnly;
        try
        {
            entriesOnly = ShouldExcludePath(path);
            if (entriesOnly && !ShouldCheckEntries(path))
                return;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to classify filesystem event path {Path}", path);
            return;
        }

        var nextDebounce = new CancellationTokenSource();
        CancellationToken token = nextDebounce.Token;
        CancellationTokenSource? previousDebounce;
        long deliveryGeneration;
        lock (_stateSync)
        {
            if (_disposed
                || sourceWatcher is not null && !ReferenceEquals(_watcher, sourceWatcher))
            {
                nextDebounce.Dispose();
                return;
            }

            deliveryGeneration = ++_stateGeneration;
            if (entriesOnly)
            {
                // The watcher uses a canonical root; the displayed items retain the requested
                // spelling (including directory aliases such as /var on macOS).
                string requestedPath = sourceWatcher is FileSystemWatcher watcher && _watchedRequestedPath is { } root
                    ? Path.GetFullPath(Path.Combine(root, Path.GetRelativePath(watcher.Path, path)))
                    : Path.GetFullPath(path);
                _pendingEntryPaths.Add(requestedPath);
            }
            else
            {
                _pendingContentChange = true;
            }
            previousDebounce = _debounceCts;
            _debounceCts = nextDebounce;
            _ = PostDeliveryAfterDelayAsync(nextDebounce, token, deliveryGeneration);
        }

        CancelAndDispose(previousDebounce);
    }

    private async Task PostDeliveryAfterDelayAsync(
        CancellationTokenSource debounce,
        CancellationToken token,
        long deliveryGeneration)
    {
        try
        {
            await Task.Delay(_debounceInterval, token).ConfigureAwait(false);
            _postDelivery(() => TryDeliver(debounce, deliveryGeneration));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ClearFailedDelivery(debounce, deliveryGeneration);
            _logger.LogWarning(ex, "Failed to deliver a file browser change notification");
        }
    }

    private void ClearFailedDelivery(
        CancellationTokenSource debounce,
        long deliveryGeneration)
    {
        bool ownsDebounce;
        lock (_stateSync)
        {
            ownsDebounce = deliveryGeneration == _stateGeneration
                           && ReferenceEquals(_debounceCts, debounce);
            if (ownsDebounce)
            {
                _debounceCts = null;
                ClearPendingChanges();
            }
        }

        if (ownsDebounce)
        {
            debounce.Dispose();
        }
    }

    private void TryDeliver(CancellationTokenSource debounce, long deliveryGeneration)
    {
        lock (_stateSync)
        {
            if (_disposed
                || deliveryGeneration != _stateGeneration
                || !ReferenceEquals(_debounceCts, debounce))
            {
                return;
            }

            _debounceCts = null;
            _errorRearmCount = 0;
            _failingCanonicalPath = null;
            debounce.Dispose();
            bool contentChanged = _pendingContentChange;
            string[] entryPaths = _pendingEntryPaths.ToArray();
            ClearPendingChanges();
            if (contentChanged)
                Changed?.Invoke();
            else
                EntriesChanged?.Invoke(entryPaths);
        }
    }

    // Called while holding _stateSync.
    private void ClearPendingChanges()
    {
        _pendingEntryPaths.Clear();
        _pendingContentChange = false;
    }

    // A delivered event resets the rearm budget.
    internal void MarkDelivered()
    {
        lock (_stateSync)
        {
            if (_disposed)
                return;

            _errorRearmCount = 0;
            _failingCanonicalPath = null;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? debounce;
        FileSystemWatcher? watcher;
        lock (_stateSync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _stateGeneration++;
            debounce = _debounceCts;
            _debounceCts = null;
            ClearPendingChanges();
            watcher = _watcher;
            _watcher = null;
            _watchedCanonicalPath = null;
            _watchedRequestedPath = null;
        }

        ReleaseDetached(debounce, watcher);
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
