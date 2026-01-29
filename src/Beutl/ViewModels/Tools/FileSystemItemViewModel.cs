using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Symbol = FluentIcons.Common.Symbol;

namespace Beutl.ViewModels.Tools;

/// <summary>
/// ファイルまたはフォルダを表すViewModel
/// </summary>
public class FileSystemItemViewModel : INotifyPropertyChanged, IDisposable
{
    private string _name;
    private bool _isExpanded;
    private bool _isRenaming;
    private bool _isSelected;
    private Bitmap? _thumbnail;
    private bool _childrenLoaded;

    public FileSystemItemViewModel(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        _name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(_name))
        {
            _name = fullPath; // Root directory
        }
        Extension = isDirectory ? string.Empty : Path.GetExtension(fullPath).ToLowerInvariant();
        IconSymbol = GetIconSymbol();

        if (isDirectory)
        {
            Children = [];
            AddPlaceholderIfNeeded();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public string Extension { get; }

    public Symbol IconSymbol { get; }

    public ObservableCollection<FileSystemItemViewModel>? Children { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                if (value && !_childrenLoaded)
                {
                    LoadChildren();
                }
            }
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming != value)
            {
                _isRenaming = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail != value)
            {
                _thumbnail?.Dispose();
                _thumbnail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    public bool HasThumbnail => _thumbnail != null;

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
            var dirInfo = new DirectoryInfo(FullPath);

            // Directories first
            foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((dir.Attributes & FileAttributes.Hidden) == 0)
                {
                    Children.Add(new FileSystemItemViewModel(dir.FullName, true));
                }
            }

            // Then files
            foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((file.Attributes & FileAttributes.Hidden) == 0)
                {
                    Children.Add(new FileSystemItemViewModel(file.FullName, false));
                }
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
            Children.Clear();
            if (_isExpanded)
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
            if (dirInfo.EnumerateFileSystemInfos().Any(
                e => (e.Attributes & FileAttributes.Hidden) == 0))
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

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _thumbnail?.Dispose();
        _thumbnail = null;

        if (Children != null)
        {
            foreach (var child in Children)
            {
                child.Dispose();
            }
            Children.Clear();
        }
    }
}
