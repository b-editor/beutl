using Avalonia.Threading;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

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
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", path);
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
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
