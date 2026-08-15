using Avalonia.Threading;
using Beutl.Editor.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

// ディレクトリ変更を監視し、デバウンス付きで変更通知を発行する。
internal sealed class DirectoryWatcherService : IDisposable
{
    private static readonly TimeSpan s_debounceInterval = TimeSpan.FromMilliseconds(300);
    private readonly ILogger _logger = Log.CreateLogger<DirectoryWatcherService>();
    // A watcher failing for a persistent reason (an exhausted inotify budget, an unreadable mount)
    // raises Error again the moment it is rebuilt, so rebuilding is capped. The cap counts
    // *consecutive* failures.
    private const int MaxErrorRearms = 3;

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private int _errorRearmCount;
    private string? _failingPath;

    // ファイルシステムに変更があったときに発火する。UIスレッドで呼び出される。
    public event Action? Changed;

    internal bool IsWatching => _watcher is not null;

    // 指定パスの監視を開始する。前回の監視は自動的に停止される。
    public void Watch(string? path) => Watch(path, isErrorRearm: false);

    private void Watch(string? path, bool isErrorRearm)
    {
        // Callers re-Watch the same path on unrelated state changes, so only a move to a different
        // folder counts as leaving the failure behind.
        if (!isErrorRearm && !string.Equals(_failingPath, path, StringComparison.Ordinal))
        {
            _errorRearmCount = 0;
            _failingPath = null;
        }

        // A recursive watcher costs an inotify descriptor per subdirectory.
        if (_watcher is not null && string.Equals(_watcher.Path, path, StringComparison.Ordinal))
            return;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        _watcher?.Dispose();
        _watcher = null;

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", path);
        }
    }

    // After an Error the watcher delivers nothing more, and Refresh never calls Watch, so a tab left
    // on the same folder would stay silently un-watched until the user navigated elsewhere.
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "FileSystemWatcher stopped; rebuilding it");

        Dispatcher.UIThread.Post(() =>
        {
            if (_watcher is not { } watcher || !ReferenceEquals(watcher, sender))
                return;

            if (TryRearmAfterError())
            {
                // Changes made while the watcher was down produced no events; resync now.
                Changed?.Invoke();
            }
        });
    }

    // Separated from the dispatcher hop so a test can drive the recovery the OS would trigger.
    internal bool TryRearmAfterError()
    {
        string? path = _watcher?.Path;
        _watcher?.Dispose();
        _watcher = null;

        if (path is null || _errorRearmCount >= MaxErrorRearms)
        {
            if (path is not null)
            {
                _logger.LogWarning(
                    "FileSystemWatcher for {Path} failed {Count} times in a row; leaving it off until the folder changes",
                    path,
                    _errorRearmCount);
            }

            return false;
        }

        _failingPath = path;

        _errorRearmCount++;
        Watch(path, isErrorRearm: true);
        return _watcher is not null;
    }

    // プロジェクト、シーン、要素のファイルは頻繁に変更されるため除外
    private bool ShouldExcludePath(string path)
    {
        // templatesディレクトリは例外
        if (PathScope.IsUnderDirectory(path, BeutlEnvironment.GetTemplatesDirectoryPath()))
        {
            return false;
        }

        // materialsディレクトリも例外
        if (PathScope.IsUnderDirectory(path, BeutlEnvironment.GetMaterialsDirectoryPath()))
        {
            return false;
        }

        return path.EndsWith(".bep", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".belm", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".beutl");
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (ShouldExcludePath(e.FullPath))
            return;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Delay(s_debounceInterval, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        MarkDelivered();
                        Changed?.Invoke();
                    },
                    DispatcherPriority.Background);
            }
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    // A delivered event proves the current watcher works, so the failures an earlier one racked up
    // must not count towards the cap.
    internal void MarkDelivered()
    {
        _errorRearmCount = 0;
        _failingPath = null;
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _watcher?.Dispose();
        _watcher = null;
    }
}
