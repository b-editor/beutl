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

        if (!IsDirectory)
        {
            _ = LoadThumbnailAsync();
            _ = LoadMediaInfoAsync();
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

    // An object template is a .json file, so the extension alone cannot say what it holds; the
    // category has to be read out of the file itself.
    // IsObjectTemplateFile only tests the extension and the directory, so a stray, half-written or
    // no-longer-resolvable .json lands here too; those fall back to the ordinary file description.
    private async Task<bool> LoadTemplateInfoAsync()
    {
        try
        {
            var item = await Task.Run(() => ObjectTemplateService.Instance.TryLoadFromFile(FullPath));
            if (item == null)
                return false;

            IconSymbol.Value = GetTemplateIconSymbol(item.BaseType);
            MediaInfoText.Value = TypeDisplayHelpers.GetLocalizedName(item.ActualType);
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
        var service = FileThumbnailService.Instance;
        if (service.IsObjectTemplateFile(FullPath))
        {
            if (await LoadTemplateInfoAsync())
                return;

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

    private Icon GetIconSymbol()
    {
        if (IsDirectory)
        {
            return Icon.Folder;
        }

        // Named extensions win over the decoder-driven classification: '.ts' is both TypeScript and
        // a transport stream, and which one Classify reports depends on whether an optional decoder
        // extension happens to be registered.
        Icon? named = Extension switch
        {
            // Document files
            ".pdf" => Icon.DocumentPdf,
            ".doc" or ".docx" => Icon.Document,
            ".txt" or ".md" or ".json" or ".xml" or ".yaml" or ".yml" => Icon.DocumentText,

            // Code files
            ".cs" or ".fs" or ".vb" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".xaml"
                or ".axaml" => Icon.Code,

            // Archive files
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" =>
                Icon.FolderZip,

            // Beutl project files
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
