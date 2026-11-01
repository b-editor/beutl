using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public class FileSystemItemViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _thumbnailCts;
    private readonly CancellationTokenSource _metadataCts = new();
    private bool _disposed;
    private bool _childrenLoaded;
    private int _mediaInfoLoad;
    private (long Length, DateTime LastWriteTime)? _detailsStamp;

    public FileSystemItemViewModel(string fullPath, bool isDirectory)
        : this(fullPath, isDirectory, loadDetails: true)
    {
    }

    private FileSystemItemViewModel(string fullPath, bool isDirectory, bool loadDetails)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = new ReactiveProperty<string>(Path.GetFileName(fullPath));
        if (string.IsNullOrEmpty(Name.Value))
        {
            Name.Value = fullPath; // Root directory
        }

        Extension = isDirectory ? string.Empty : Path.GetExtension(fullPath).ToLowerInvariant();
        IconSymbol = new ReactiveProperty<Icon>(GetIconSymbol()).AddTo(_disposables);

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

        if (!IsDirectory && loadDetails)
        {
            ReloadDetailsIfChanged();
        }
    }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public string Extension { get; }

    public ReactiveProperty<Icon> IconSymbol { get; }

    public ObservableCollection<FileSystemItemViewModel>? Children { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(false);

    public ReactiveProperty<Bitmap?> Thumbnail { get; } = new((Bitmap?)null);

    public ReadOnlyReactivePropertySlim<bool> HasThumbnail { get; }

    // メディア情報のツールチップテキスト（遅延ロード）
    public ReactiveProperty<string?> MediaInfoText { get; } = new((string?)null);

    // A template's category is read from the file; a .json that is not a loadable template
    // falls back to the file description.
    private async Task<bool> LoadTemplateInfoAsync(int load)
    {
        try
        {
            var item = await Task.Run(() => ObjectTemplateService.Instance.TryLoadFromFile(FullPath));
            if (item == null)
                return false;

            if (!_disposed && load == _mediaInfoLoad)
            {
                IconSymbol.Value = GetTemplateIconSymbol(item.BaseType);
                MediaInfoText.Value = TypeDisplayHelpers.GetLocalizedName(item.ActualType);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Icon GetTemplateIconSymbol(Type baseType)
    {
        if (baseType.IsAssignableTo(typeof(Element))) return Icon.Filmstrip;
        if (baseType.IsAssignableTo(typeof(ElementSet))) return Icon.Layer;
        if (baseType.IsAssignableTo(typeof(FilterEffect))) return Icon.Wand;
        if (baseType.IsAssignableTo(typeof(Transform))) return Icon.ArrowMove;
        if (baseType.IsAssignableTo(typeof(Drawable))) return Icon.Shapes;
        if (baseType.IsAssignableTo(typeof(Sound)) || baseType.IsAssignableTo(typeof(AudioEffect)))
            return Icon.MusicNote1;
        if (baseType.IsAssignableTo(typeof(Media.Brush))) return Icon.PaintBrush;
        if (baseType.IsAssignableTo(typeof(Media.Geometry))) return Icon.Pen;
        if (baseType.IsAssignableTo(typeof(Media.Pen))) return Icon.LineHorizontal1;

        return Icon.Document;
    }

    private async Task LoadMediaInfoAsync()
    {
        int load = Interlocked.Increment(ref _mediaInfoLoad);
        var service = FileThumbnailService.Instance;
        if (service.IsObjectTemplateFile(FullPath))
        {
            if (await LoadTemplateInfoAsync(load) || _disposed || load != _mediaInfoLoad)
                return;

            IconSymbol.Value = GetIconSymbol();
            SetFileDescription();
            return;
        }

        if (!service.CanGetMediaInfo(FullPath))
        {
            SetFileDescription();
            return;
        }

        try
        {
            // A reload reads the file as it is now, so an earlier load still running must not
            // replace what the reload shows.
            CancellationToken token = _metadataCts.Token;
            var info = await service.GetMediaInfoAsync(FullPath, token);
            if (!token.IsCancellationRequested && info != null && load == Volatile.Read(ref _mediaInfoLoad))
            {
                MediaInfoText.Value = info.ToDisplayString();
            }
        }
        catch
        {
        }
    }

    private void SetFileDescription()
    {
        try
        {
            var fileInfo = new FileInfo(FullPath);
            MediaInfoText.Value =
                $"{MediaFileInfo.FormatFileSize(fileInfo.Length)} · {fileInfo.LastWriteTime:yyyy/MM/dd}";
        }
        catch
        {
        }
    }

    private async Task LoadThumbnailAsync()
    {
        var service = FileThumbnailService.Instance;
        if (!service.CanGenerateThumbnail(FullPath))
            return;

        try
        {
            _thumbnailCts?.Cancel();
            _thumbnailCts?.Dispose();
            _thumbnailCts = new CancellationTokenSource();
            CancellationToken token = _thumbnailCts.Token;
            var bitmap = await service.GetThumbnailAsync(FullPath, token);
            if (!token.IsCancellationRequested)
            {
                Thumbnail.Value = bitmap;
            }
        }
        catch
        {
        }
    }

    private Icon GetIconSymbol()
    {
        if (IsDirectory)
        {
            return Icon.Folder;
        }

        // Named extensions win over the decoder-driven classification, so a decoder claiming an
        // extension this list already names cannot change the icon out from under it.
        Icon? named = Extension switch
        {
            ".pdf" => Icon.DocumentPdf,
            ".doc" or ".docx" => Icon.Document,
            ".txt" or ".md" or ".json" or ".xml" or ".yaml" or ".yml" => Icon.DocumentText,

            ".cs" or ".fs" or ".vb" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".xaml"
                or ".axaml" => Icon.Code,

            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" =>
                Icon.FolderZip,

            ".bep" => Icon.Folder,
            ".scene" => Icon.Filmstrip,
            ".belm" => Icon.Shapes,

            _ => null
        };

        if (named is { } icon)
        {
            return icon;
        }

        return DecoderFileExtensions.Classify(FullPath) switch
        {
            MediaFileKind.Image => Icon.Image,
            MediaFileKind.Video => Icon.Video,
            MediaFileKind.Audio => Icon.MusicNote1,
            _ => Icon.Document
        };
    }

    public void LoadChildren()
    {
        if (!IsDirectory || Children == null || _childrenLoaded)
            return;

        _childrenLoaded = true;
        Children.Clear();

        // A folder that cannot be read is shown without children.
        try
        {
            foreach (var item in FileSystemEnumerator.EnumerateDirectory(FullPath))
            {
                Children.Add(item);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    public void Refresh()
    {
        if (IsDirectory && Children != null)
        {
            _childrenLoaded = false;
            FileSystemEnumerator.DisposeAndClear(Children);
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

    internal void RefreshEntriesForDirectory(string directory)
    {
        if (!IsDirectory || Children is null)
            return;

        if (string.Equals(FullPath, directory, StringComparison.Ordinal))
        {
            bool changed = _childrenLoaded
                ? FileSystemEnumerator.HasEntriesChanged(Children, FullPath)
                : (Children.Count > 0) != new DirectoryInfo(FullPath)
                    .EnumerateFileSystemInfos().Any(FileSystemEnumerator.IsVisible);
            if (changed) Refresh();
        }
        else if (_childrenLoaded)
        {
            foreach (var child in Children)
                child.RefreshEntriesForDirectory(directory);
        }
    }

    // Catch up the displayed tree after asynchronous watcher startup, without walking unopened subtrees.
    internal void RefreshLoadedEntries(bool includeChildren = true)
    {
        if (_disposed) return;
        if (!IsDirectory)
        {
            ReloadDetailsIfChanged();
            return;
        }
        if (!includeChildren) return;

        try
        {
            if (_childrenLoaded && Children is not null)
            {
                FileSystemEnumerator.RefreshCollection(Children, FullPath);
                return;
            }
            RefreshEntriesForDirectory(FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An inaccessible item must not stop catch-up for its siblings or loaded children.
        }
        if (_childrenLoaded && Children is not null)
        {
            foreach (var child in Children)
                child.RefreshLoadedEntries();
        }
    }

    private void ReloadDetailsIfChanged()
    {
        try
        {
            var info = new FileInfo(FullPath);
            var stamp = (info.Length, info.LastWriteTimeUtc);
            if (_detailsStamp == stamp) return;
            _detailsStamp = stamp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _ = LoadThumbnailAsync();
        _ = LoadMediaInfoAsync();
    }

    // Reads the size and details of the listed files at or below the given paths again.
    internal void ReloadDetailsAtOrBelow(IReadOnlyCollection<string> paths)
    {
        if (_disposed)
            return;

        if (!IsDirectory)
        {
            if (paths.Any(path => FilePathComparison.IsSameOrDescendantCanonicalPath(path, FullPath)))
                _ = LoadMediaInfoAsync();
        }
        else if (_childrenLoaded && Children is not null)
        {
            foreach (FileSystemItemViewModel child in Children)
                child.ReloadDetailsAtOrBelow(paths);
        }
    }

    private void AddPlaceholderIfNeeded()
    {
        try
        {
            var dirInfo = new DirectoryInfo(FullPath);
            if (dirInfo.EnumerateFileSystemInfos().Any(FileSystemEnumerator.IsVisible))
            {
                // プレースホルダーを追加して展開矢印を表示させる
                Children!.Add(new FileSystemItemViewModel(FullPath, false, loadDetails: false));
            }
        }
        catch
        {
            // アクセスエラーの場合はプレースホルダーなし（展開矢印非表示）
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _metadataCts.Cancel();
        _metadataCts.Dispose();
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;

        Thumbnail.Value = null;

        if (Children != null)
        {
            FileSystemEnumerator.DisposeAndClear(Children);
        }

        _disposables.Dispose();
    }
}
