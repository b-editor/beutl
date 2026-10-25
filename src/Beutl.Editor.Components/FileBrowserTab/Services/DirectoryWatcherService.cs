using System.Collections.Concurrent;
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
    // Only background work takes this lock; starting and disposing the same watcher must not overlap.
    private readonly object _watchSetupSync = new();
    private readonly TimeSpan _debounceInterval;
    private readonly Action<Action> _postDelivery;
    private readonly Action<FileSystemWatcher> _startWatcher;
    private readonly ConcurrentDictionary<string, string> _configuredSpecialDirectoryIdentities =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DirectoryIdentity> _directoryIdentities =
        new(StringComparer.Ordinal);
    // Limit consecutive rearm attempts because persistent failures recur immediately.
    private const int MaxErrorRearms = 3;

    private FileSystemWatcher? _watcher;
    private bool _watcherFailed;
    private string? _watchedCanonicalPath;
    private string? _watchedRequestedPath;
    private CancellationTokenSource? _debounceCts;
    private readonly HashSet<string> _pendingEntryPaths = new(StringComparer.Ordinal);
    private bool _pendingContentChange;
    private int _errorRearmCount;
    private string? _failingCanonicalPath;
    private bool _disposed;
    private long _stateGeneration;
    private long _watchRequest;
    private long _watcherRequest;

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
    public Task WatchAsync(string? path, bool includeSubdirectories = true)
    {
        long request;
        CancellationTokenSource? debounce;
        lock (_stateSync)
        {
            if (_disposed)
                return Task.CompletedTask;

            request = ++_watchRequest;
            // Navigation takes effect now, even if native setup is still busy with the old folder.
            _stateGeneration++;
            debounce = _debounceCts;
            _debounceCts = null;
            ClearPendingChanges();
        }
        CancelAndDispose(debounce);

        // Starting a watcher walks the subtree on Linux and calls sync() on macOS.
        // Reserve the request before queuing it so a slow start cannot undo later navigation.
        return Task.Run(() =>
        {
            lock (_watchSetupSync)
            {
                Watch(path, includeSubdirectories, isErrorRearm: false, request);
            }
        });
    }

    private void Watch(string? path, bool includeSubdirectories, bool isErrorRearm, long request)
    {
        FileSystemWatcher? currentWatcher;
        string? watchedCanonicalPath;
        lock (_stateSync)
        {
            if (_disposed || request != _watchRequest)
                return;

            currentWatcher = _watcher;
            watchedCanonicalPath = _watchedCanonicalPath;
        }

        bool pathResolved = TryResolveWatchPath(path, out string? canonicalPath);

        // Only changing folders clears a failure budget.
        if (!isErrorRearm)
            ResetErrorBudget(pathResolved, canonicalPath, request);

        // Recursive watchers consume one inotify descriptor per subdirectory.
        if (currentWatcher is not null
            && currentWatcher.IncludeSubdirectories == includeSubdirectories
            && pathResolved
            && canonicalPath is not null
            && Directory.Exists(canonicalPath)
            && string.Equals(
                watchedCanonicalPath,
                canonicalPath,
                StringComparison.Ordinal)
            && TryKeepCurrentWatcher(currentWatcher, canonicalPath, path!, request))
        {
            return;
        }

        if (!TryDetachWatcher(
                out long watchGeneration,
                out CancellationTokenSource? previousDebounce,
                out FileSystemWatcher? previousWatcher,
                request))
        {
            return;
        }

        ReleaseDetached(previousDebounce, previousWatcher);

        if (!pathResolved || canonicalPath is null || !Directory.Exists(canonicalPath))
            return;

        FileSystemWatcher? nextWatcher = null;
        try
        {
            nextWatcher = CreateWatcher(canonicalPath, includeSubdirectories);
            if (TryInstallWatcher(nextWatcher, watchGeneration, canonicalPath, path!, request))
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

    private void ResetErrorBudget(bool pathResolved, string? canonicalPath, long request)
    {
        lock (_stateSync)
        {
            if (_disposed || request != _watchRequest)
                return;

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
    private bool TryKeepCurrentWatcher(FileSystemWatcher currentWatcher, string canonicalPath, string path, long request)
    {
        lock (_stateSync)
        {
            if (!_disposed
                && request == _watchRequest
                && !_watcherFailed
                && ReferenceEquals(_watcher, currentWatcher)
                && string.Equals(
                    _watchedCanonicalPath,
                    canonicalPath,
                    StringComparison.Ordinal))
            {
                _watchedRequestedPath = Path.GetFullPath(path);
                _watcherRequest = request;
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
        out FileSystemWatcher? watcher,
        long request)
    {
        lock (_stateSync)
        {
            if (_disposed || request != _watchRequest)
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
        _directoryIdentities.Clear();
    }

    private FileSystemWatcher CreateWatcher(string canonicalPath, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(canonicalPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            IncludeSubdirectories = includeSubdirectories,
        };

        watcher.Created += OnFileSystemEvent;
        watcher.Deleted += OnFileSystemEvent;
        watcher.Renamed += OnFileSystemEvent;
        watcher.Changed += OnFileSystemEvent;
        watcher.Error += OnWatcherError;
        return watcher;
    }

    // False when a later Watch or Dispose superseded this one; the caller then still owns the watcher.
    private bool TryInstallWatcher(FileSystemWatcher watcher, long generation, string canonicalPath, string path, long request)
    {
        lock (_stateSync)
        {
            if (_disposed || request != _watchRequest || _stateGeneration != generation)
                return false;

            _watcher = watcher;
            _watcherFailed = false;
            _watcherRequest = request;
            _watchedCanonicalPath = canonicalPath;
            _watchedRequestedPath = Path.GetFullPath(path);
        }

        try
        {
            // Publish first so errors raised during startup can rearm, but never hold the UI's
            // state lock while the operating system starts the watcher.
            _startWatcher(watcher);
            return true;
        }
        catch
        {
            lock (_stateSync)
            {
                if (ReferenceEquals(_watcher, watcher))
                {
                    _watcher = null;
                    _watchedCanonicalPath = null;
                    _watchedRequestedPath = null;
                }
            }
            throw;
        }
    }

    // Rebuild after Error; otherwise the current folder stays unwatched.
    internal async void OnWatcherError(object sender, ErrorEventArgs e)
    {
        long request;
        lock (_stateSync)
        {
            if (_disposed || !ReferenceEquals(_watcher, sender))
                return;
            // A newer same-path request may supersede the repair, but cannot reuse this watcher.
            _watcherFailed = true;
            if (!IsCurrentWatcher(sender))
                return;
            request = _watchRequest;
        }

        _logger.LogWarning(e.GetException(), "FileSystemWatcher stopped; rebuilding it");

        if (await TryRearmAfterErrorAsync(sender))
        {
            _postDelivery(() =>
            {
                lock (_stateSync)
                {
                    // Resync changes missed while the watcher was down.
                    if (!_disposed && request == _watchRequest)
                        Changed?.Invoke();
                }
            });
        }
    }

    internal Task<bool> TryRearmAfterErrorAsync(object? sourceWatcher = null)
    {
        return Task.Run(() =>
        {
            lock (_watchSetupSync)
            {
                return TryRearmAfterError(sourceWatcher);
            }
        });
    }

    private bool TryRearmAfterError(object? sourceWatcher)
    {
        string? requestedPath;
        string? canonicalPath;
        FileSystemWatcher? watcher;
        CancellationTokenSource? debounce;
        long request;
        bool includeSubdirectories;
        lock (_stateSync)
        {
            if (_disposed || sourceWatcher is not null && !IsCurrentWatcher(sourceWatcher))
                return false;

            request = _watchRequest;
            watcher = _watcher;
            includeSubdirectories = watcher?.IncludeSubdirectories ?? true;
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
        _configuredSpecialDirectoryIdentities.Clear();
        _directoryIdentities.Clear();

        if (requestedPath is null || canonicalPath is null)
        {
            return false;
        }

        lock (_stateSync)
        {
            if (_disposed || request != _watchRequest)
                return false;

            _failingCanonicalPath ??= canonicalPath;
        }

        // Watch swallows a construction failure, and with no watcher no further Error can arrive to
        // spend what is left of the budget.
        while (true)
        {
            lock (_stateSync)
            {
                if (_disposed || request != _watchRequest || _errorRearmCount >= MaxErrorRearms)
                {
                    break;
                }

                _errorRearmCount++;
            }

            Watch(requestedPath, includeSubdirectories, isErrorRearm: true, request);
            lock (_stateSync)
            {
                if (request != _watchRequest)
                    return false;
                if (_watcher is not null)
                    return true;
            }
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
            return !_disposed && _watcherRequest == _watchRequest && ReferenceEquals(_watcher, sender);
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

    // Test seam: tests raise events without a watcher, so the null sourceWatcher branches serve only them.
    internal void NotifyPathChanged(string path) => NotifyPathChanged(path, sourceWatcher: null);

    private void NotifyPathChanged(string path, object? sourceWatcher)
    {
        lock (_stateSync)
        {
            if (_disposed || sourceWatcher is not null && !IsCurrentWatcher(sourceWatcher))
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
                || sourceWatcher is not null && !IsCurrentWatcher(sourceWatcher))
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

        // A start already in progress owns the watcher until it returns. Closing the tab must
        // invalidate callbacks immediately without waiting for that filesystem work.
        _ = Task.Run(() =>
        {
            lock (_watchSetupSync)
            {
                ReleaseDetached(debounce, watcher);
            }
        });
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
