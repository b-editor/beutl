using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Beutl.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Symbol = FluentIcons.Common.Symbol;

namespace Beutl.ViewModels.Tools;

// ファイルまたはフォルダを表すViewModel
public class FileSystemItemViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _thumbnailCts;
    private bool _childrenLoaded;

    public FileSystemItemViewModel(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = new ReactiveProperty<string>(Path.GetFileName(fullPath));
        if (string.IsNullOrEmpty(Name.Value))
        {
            Name.Value = fullPath; // Root directory
        }

        Extension = isDirectory ? string.Empty : Path.GetExtension(fullPath).ToLowerInvariant();
        IconSymbol = GetIconSymbol();

        if (isDirectory)
        {
            Children = [];
            AddPlaceholderIfNeeded();
        }

        IsExpanded.Subscribe(value =>
        {
            if (value && !_childrenLoaded)
            {
                LoadChildren();
            }
        }).AddTo(_disposables);

        HasThumbnail = Thumbnail.Select(t => t != null)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        if (!IsDirectory)
        {
            _ = LoadThumbnailAsync();
            _ = LoadMediaInfoAsync();
        }
    }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public string Extension { get; }

    public Symbol IconSymbol { get; }

    public ObservableCollection<FileSystemItemViewModel>? Children { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(false);

    public ReactiveProperty<Bitmap?> Thumbnail { get; } = new((Bitmap?)null);

    public ReadOnlyReactivePropertySlim<bool> HasThumbnail { get; }

    // メディア情報のツールチップテキスト（遅延ロード）
    public ReactiveProperty<string?> MediaInfoText { get; } = new((string?)null);

    private async Task LoadMediaInfoAsync()
    {
        var service = FileThumbnailService.Instance;
        if (!service.CanGetMediaInfo(FullPath))
        {
            // メディアでないファイルはサイズと日時を表示
            try
            {
                var fileInfo = new FileInfo(FullPath);
                MediaInfoText.Value =
                    $"{MediaFileInfo.FormatFileSize(fileInfo.Length)} · {fileInfo.LastWriteTime:yyyy/MM/dd}";
            }
            catch
            {
                // ignore
            }

            return;
        }

        try
        {
            var info = await service.GetMediaInfoAsync(FullPath);
            if (info != null)
            {
                MediaInfoText.Value = info.ToDisplayString();
            }
        }
        catch
        {
            // ignore
        }
    }

    private async Task LoadThumbnailAsync()
    {
        var service = FileThumbnailService.Instance;
        if (!service.CanGenerateThumbnail(FullPath))
            return;

        try
        {
            _thumbnailCts = new CancellationTokenSource();
            var bitmap = await service.GetThumbnailAsync(FullPath, _thumbnailCts.Token);
            if (bitmap != null)
            {
                Thumbnail.Value = bitmap;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private Symbol GetIconSymbol()
    {
        if (IsDirectory)
        {
            return Symbol.Folder;
        }

        return Extension switch
        {
            // Image files
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tiff" or ".tif" =>
                Symbol.Image,

            // Video files
            ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" or ".flv" or ".webm" =>
                Symbol.Video,

            // Audio files
            ".mp3" or ".wav" or ".ogg" or ".flac" or ".aac" or ".wma" or ".m4a" =>
                Symbol.MusicNote1,

            // Document files
            ".pdf" => Symbol.DocumentPdf,
            ".doc" or ".docx" => Symbol.Document,
            ".txt" or ".md" or ".json" or ".xml" or ".yaml" or ".yml" => Symbol.DocumentText,

            // Code files
            ".cs" or ".fs" or ".vb" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".xaml" or ".axaml" =>
                Symbol.Code,

            // Archive files
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" =>
                Symbol.FolderZip,

            // Beutl project files
            ".bepj" => Symbol.Folder,
            ".besc" => Symbol.Filmstrip,

            // Default
            _ => Symbol.Document
        };
    }

    public void LoadChildren()
    {
        if (!IsDirectory || Children == null || _childrenLoaded)
            return;

        _childrenLoaded = true;
        Children.Clear();

        try
        {
            foreach (var item in FileSystemEnumerator.EnumerateDirectory(FullPath))
            {
                Children.Add(item);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore directories we can't access
        }
        catch (IOException)
        {
            // Ignore IO errors
        }
    }

    public void Refresh()
    {
        if (IsDirectory && Children != null)
        {
            _childrenLoaded = false;
            foreach (var child in Children)
            {
                child.Dispose();
            }

            Children.Clear();
            if (IsExpanded.Value)
            {
                LoadChildren();
            }
            else
            {
                AddPlaceholderIfNeeded();
            }
        }
    }

    private void AddPlaceholderIfNeeded()
    {
        try
        {
            var dirInfo = new DirectoryInfo(FullPath);
            if (dirInfo.EnumerateFileSystemInfos().Any(e => (e.Attributes & FileAttributes.Hidden) == 0))
            {
                // プレースホルダーを追加して展開矢印を表示させる
                Children!.Add(new FileSystemItemViewModel(FullPath, false));
            }
        }
        catch
        {
            // アクセスエラーの場合はプレースホルダーなし（展開矢印非表示）
        }
    }

    public void Dispose()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;

        Thumbnail.Value = null;

        if (Children != null)
        {
            foreach (var child in Children)
            {
                child.Dispose();
            }

            Children.Clear();
        }

        _disposables.Dispose();
    }
}
