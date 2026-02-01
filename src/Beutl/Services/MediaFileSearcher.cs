using System.Collections.ObjectModel;
using Avalonia.Threading;
using Beutl.Logging;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

// プロジェクトディレクトリ内のメディアファイルを再帰的に検索する。
internal sealed class MediaFileSearcher : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<MediaFileSearcher>();
    private CancellationTokenSource? _searchCts;

    // 検索結果のメディアファイルアイテム
    public ObservableCollection<FileSystemItemViewModel> MediaFileItems { get; } = [];

    // メディアファイル検索中かどうか
    public ReactivePropertySlim<bool> IsLoadingMediaFiles { get; } = new(false);

    // メディアファイルが見つからなかったか
    public ReactivePropertySlim<bool> HasNoMediaFiles { get; } = new(false);

    // 指定ディレクトリ内のメディアファイルを非同期で検索する。
    // 前回の検索はキャンセルされる。
    public void SearchAsync(string? searchDirectory)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        DisposeAndClearItems();
        IsLoadingMediaFiles.Value = true;
        HasNoMediaFiles.Value = false;

        if (string.IsNullOrEmpty(searchDirectory) || !Directory.Exists(searchDirectory))
        {
            IsLoadingMediaFiles.Value = false;
            HasNoMediaFiles.Value = true;
            return;
        }

        Task.Run(() =>
        {
            var results = new List<string>();
            try
            {
                SearchRecursive(searchDirectory, results, token, maxCount: 200);

                if (token.IsCancellationRequested)
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    foreach (string filePath in results)
                    {
                        MediaFileItems.Add(new FileSystemItemViewModel(filePath, false));
                    }

                    IsLoadingMediaFiles.Value = false;
                    HasNoMediaFiles.Value = MediaFileItems.Count == 0;
                });
            }
            catch (OperationCanceledException)
            {
                // cancelled, no action needed
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching media files in {Path}", searchDirectory);
                Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingMediaFiles.Value = false;
                    HasNoMediaFiles.Value = true;
                });
            }
        }, token);
    }

    private static void SearchRecursive(string directory, List<string> results, CancellationToken token, int maxCount)
    {
        token.ThrowIfCancellationRequested();

        try
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                token.ThrowIfCancellationRequested();
                if (results.Count >= maxCount) return;

                if (FileThumbnailService.Instance.IsMediaFile(file))
                {
                    results.Add(file);
                }
            }

            foreach (string subDir in Directory.GetDirectories(directory))
            {
                token.ThrowIfCancellationRequested();
                if (results.Count >= maxCount) return;

                try
                {
                    var dirInfo = new DirectoryInfo(subDir);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == 0)
                    {
                        SearchRecursive(subDir, results, token, maxCount);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // skip inaccessible directories
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // skip inaccessible directories
        }
    }

    private void DisposeAndClearItems()
    {
        foreach (var item in MediaFileItems)
        {
            item.Dispose();
        }
        MediaFileItems.Clear();
    }

    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        DisposeAndClearItems();
    }
}
