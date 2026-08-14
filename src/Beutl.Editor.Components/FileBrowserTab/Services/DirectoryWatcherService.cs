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
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;

    // ファイルシステムに変更があったときに発火する。UIスレッドで呼び出される。
    public event Action? Changed;

    // 指定パスの監視を開始する。前回の監視は自動的に停止される。
    public void Watch(string? path)
    {
        // A recursive watcher costs an inotify descriptor per subdirectory, so never rebuild one for
        // a path already being watched. OnWatcherError drops the watcher when the OS stops
        // delivering, which is what lets a later Watch of the same path re-arm it.
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

    // After an Error the watcher delivers nothing more. Drop it so the same-path guard in Watch stops
    // matching and the next call rebuilds, instead of leaving the tab silently un-watched.
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "FileSystemWatcher stopped; it will be rebuilt on the next Watch");

        Dispatcher.UIThread.Post(() =>
        {
            if (_watcher is { } watcher && ReferenceEquals(watcher, sender))
            {
                watcher.Dispose();
                _watcher = null;
            }
        });
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
                Dispatcher.UIThread.Post(() => Changed?.Invoke(), DispatcherPriority.Background);
            }
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _watcher?.Dispose();
        _watcher = null;
    }
}
