using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public enum FileExplorerDisplayMode
{
    [Description("List")]
    List,
    [Description("Icons")]
    Icons,
    [Description("Tree")]
    Tree
}

public class FileSystemItemViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private Bitmap? _thumbnail;
    private bool _thumbnailLoaded;
    
    public required string Name { get; init; }
    
    public required string FullPath { get; init; }
    
    public required bool IsDirectory { get; init; }
    
    public long Size { get; init; }
    
    public DateTime LastModified { get; init; }
    
    public string? Extension { get; init; }
    
    public ObservableCollection<FileSystemItemViewModel>? Children { get; init; }
    
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail != value)
            {
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }
    }
    
    public bool ThumbnailLoaded
    {
        get => _thumbnailLoaded;
        set
        {
            if (_thumbnailLoaded != value)
            {
                _thumbnailLoaded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailLoaded)));
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FileExplorerViewModel : IDisposable, IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly DirectoryInfo _rootDirectory;
    private readonly Dictionary<string, Bitmap> _thumbnailCache = new();
    private readonly SemaphoreSlim _thumbnailSemaphore = new(3); // 同時に3つまでサムネイル生成
    
    public FileExplorerViewModel(EditViewModel editViewModel)
    {
        _ = editViewModel;
        
        string projectDir = Path.GetDirectoryName(editViewModel.Project.FileName) ?? Environment.CurrentDirectory;
        _rootDirectory = new DirectoryInfo(projectDir);
        
        CurrentPath = new ReactivePropertySlim<string>(_rootDirectory.FullName);
        SelectedItem = new ReactivePropertySlim<FileSystemItemViewModel?>();
        DisplayMode = new ReactivePropertySlim<FileExplorerDisplayMode>(FileExplorerDisplayMode.List);
        
        Items = new ObservableCollection<FileSystemItemViewModel>();
        TreeItems = new ObservableCollection<FileSystemItemViewModel>();
        SearchResults = new ObservableCollection<FileSystemItemViewModel>();
        
        NavigateToParentCommand = new ReactiveCommand()
            .WithSubscribe(() => NavigateToParent())
            .DisposeWith(_disposables);
        
        NavigateToDirectoryCommand = new ReactiveCommand<FileSystemItemViewModel>()
            .WithSubscribe(item =>
            {
                if (item?.IsDirectory == true)
                {
                    NavigateToDirectory(item.FullPath);
                }
            })
            .DisposeWith(_disposables);
        
        RefreshCommand = new ReactiveCommand()
            .WithSubscribe(() => RefreshItems())
            .DisposeWith(_disposables);
        
        CurrentPath.Subscribe(_ => RefreshItems())
            .DisposeWith(_disposables);
        
        DisplayMode.Subscribe(mode =>
        {
            RefreshItems();
            if (mode == FileExplorerDisplayMode.Icons)
            {
                _ = LoadThumbnailsAsync();
            }
        })
        .DisposeWith(_disposables);
        
        RefreshItems();
    }
    
    public IReactiveProperty<string> CurrentPath { get; }
    
    public IReactiveProperty<FileSystemItemViewModel?> SelectedItem { get; }
    
    public IReactiveProperty<FileExplorerDisplayMode> DisplayMode { get; }
    
    public ObservableCollection<FileSystemItemViewModel> Items { get; }
    
    public ObservableCollection<FileSystemItemViewModel> TreeItems { get; }
    
    public ObservableCollection<FileSystemItemViewModel> SearchResults { get; }
    
    public IReactiveProperty<string> SearchText { get; } = new ReactivePropertySlim<string>(string.Empty);
    
    public ReactiveCommand NavigateToParentCommand { get; }
    
    public ReactiveCommand<FileSystemItemViewModel> NavigateToDirectoryCommand { get; }
    
    public ReactiveCommand RefreshCommand { get; }
    
    private void RefreshItems()
    {
        Items.Clear();
        TreeItems.Clear();
        
        try
        {
            var directory = new DirectoryInfo(CurrentPath.Value);
            if (!directory.Exists)
                return;
            
            if (DisplayMode.Value == FileExplorerDisplayMode.Tree)
            {
                // ツリー表示の場合はルートから構築
                LoadTreeItems();
            }
            else
            {
                // リストまたはアイコン表示
                foreach (var dir in directory.GetDirectories())
                {
                    if ((dir.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    
                    Items.Add(new FileSystemItemViewModel
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        LastModified = dir.LastWriteTime,
                        Children = new ObservableCollection<FileSystemItemViewModel>()
                    });
                }
                
                foreach (var file in directory.GetFiles())
                {
                    if ((file.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    
                    Items.Add(new FileSystemItemViewModel
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
                
                // アイコン表示モードの場合はサムネイルを読み込む
                if (DisplayMode.Value == FileExplorerDisplayMode.Icons)
                {
                    _ = LoadThumbnailsAsync();
                }
            }
        }
        catch
        {
            // Handle permission errors silently
        }
    }
    
    private void LoadTreeItems()
    {
        try
        {
            var rootItem = CreateTreeItem(_rootDirectory);
            if (rootItem != null)
            {
                TreeItems.Add(rootItem);
                ExpandToCurrentPath(rootItem);
            }
        }
        catch
        {
            // Handle errors silently
        }
    }
    
    private FileSystemItemViewModel? CreateTreeItem(DirectoryInfo directory, bool loadChildren = true)
    {
        try
        {
            var item = new FileSystemItemViewModel
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                LastModified = directory.LastWriteTime,
                Children = new ObservableCollection<FileSystemItemViewModel>()
            };
            
            if (loadChildren)
            {
                foreach (var subDir in directory.GetDirectories())
                {
                    if ((subDir.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    
                    var childItem = CreateTreeItem(subDir, false);
                    if (childItem != null)
                    {
                        item.Children.Add(childItem);
                    }
                }
                
                foreach (var file in directory.GetFiles())
                {
                    if ((file.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    
                    item.Children.Add(new FileSystemItemViewModel
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
            }
            
            return item;
        }
        catch
        {
            return null;
        }
    }
    
    private void ExpandToCurrentPath(FileSystemItemViewModel item)
    {
        if (CurrentPath.Value.StartsWith(item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            item.IsExpanded = true;
            
            if (item.Children != null && item.FullPath != CurrentPath.Value)
            {
                foreach (var child in item.Children)
                {
                    if (child.IsDirectory)
                    {
                        LoadChildrenIfNeeded(child);
                        ExpandToCurrentPath(child);
                    }
                }
            }
        }
    }
    
    private void LoadChildrenIfNeeded(FileSystemItemViewModel item)
    {
        if (item.IsDirectory && item.Children != null && item.Children.Count == 0)
        {
            var dir = new DirectoryInfo(item.FullPath);
            var newItem = CreateTreeItem(dir, true);
            if (newItem?.Children != null)
            {
                foreach (var child in newItem.Children)
                {
                    item.Children.Add(child);
                }
            }
        }
    }
    
    public void NavigateToDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            CurrentPath.Value = path;
        }
    }
    
    public void NavigateToParent()
    {
        var current = new DirectoryInfo(CurrentPath.Value);
        if (current.Parent != null)
        {
            CurrentPath.Value = current.Parent.FullName;
        }
    }
    
    public async Task Search(string searchText, CancellationToken cancellationToken)
    {
        SearchResults.Clear();
        
        if (string.IsNullOrWhiteSpace(searchText))
            return;
        
        await Task.Run(() =>
        {
            try
            {
                var directory = new DirectoryInfo(CurrentPath.Value);
                SearchDirectory(directory, searchText, SearchResults, cancellationToken);
            }
            catch
            {
                // Handle errors silently
            }
        }, cancellationToken);
    }
    
    private void SearchDirectory(DirectoryInfo directory, string searchText, ObservableCollection<FileSystemItemViewModel> results, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
        
        try
        {
            foreach (var file in directory.GetFiles())
            {
                if (file.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new FileSystemItemViewModel
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
            }
            
            foreach (var dir in directory.GetDirectories())
            {
                if ((dir.Attributes & FileAttributes.Hidden) != 0)
                    continue;
                
                if (dir.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new FileSystemItemViewModel
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        LastModified = dir.LastWriteTime,
                        Children = new ObservableCollection<FileSystemItemViewModel>()
                    });
                }
                
                SearchDirectory(dir, searchText, results, cancellationToken);
            }
        }
        catch
        {
            // Skip directories we can't access
        }
    }
    
    private async Task LoadThumbnailsAsync()
    {
        var imageTasks = new List<Task>();
        var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
        var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".flv", ".wmv" };
        
        foreach (var item in Items.Where(i => !i.IsDirectory && !i.ThumbnailLoaded))
        {
            var ext = item.Extension?.ToLowerInvariant();
            if (ext != null && (imageExtensions.Contains(ext) || videoExtensions.Contains(ext)))
            {
                imageTasks.Add(LoadThumbnailForItemAsync(item, videoExtensions.Contains(ext)));
            }
        }
        
        await Task.WhenAll(imageTasks);
    }
    
    private async Task LoadThumbnailForItemAsync(FileSystemItemViewModel item, bool isVideo)
    {
        try
        {
            // キャッシュチェック
            if (_thumbnailCache.TryGetValue(item.FullPath, out var cached))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    item.Thumbnail = cached;
                    item.ThumbnailLoaded = true;
                });
                return;
            }
            
            await _thumbnailSemaphore.WaitAsync();
            try
            {
                Bitmap? thumbnail = null;
                
                if (isVideo)
                {
                    thumbnail = await LoadVideoThumbnailAsync(item.FullPath);
                }
                else
                {
                    thumbnail = await LoadImageThumbnailAsync(item.FullPath);
                }
                
                if (thumbnail != null)
                {
                    _thumbnailCache[item.FullPath] = thumbnail;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        item.Thumbnail = thumbnail;
                        item.ThumbnailLoaded = true;
                    });
                }
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }
        catch
        {
            // サムネイル生成エラーは無視
            item.ThumbnailLoaded = true;
        }
    }
    
    private async Task<Bitmap?> LoadImageThumbnailAsync(string path)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                var bitmap = new Bitmap(stream);
                
                // 96x96のサムネイルを作成
                const int thumbnailSize = 96;
                var aspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
                int width, height;
                
                if (aspect > 1)
                {
                    width = thumbnailSize;
                    height = (int)(thumbnailSize / aspect);
                }
                else
                {
                    height = thumbnailSize;
                    width = (int)(thumbnailSize * aspect);
                }
                
                return bitmap.CreateScaledBitmap(new Avalonia.PixelSize(width, height));
            }
            catch
            {
                return null;
            }
        });
    }
    
    private async Task<Bitmap?> LoadVideoThumbnailAsync(string path)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var reader = MediaReader.Open(path);
                if (reader?.VideoInfo != null)
                {
                    // 最初のフレームを取得
                    if (reader.ReadVideo(0, out var frame) && frame != null)
                    {
                        using (frame)
                        {
                            // サムネイルサイズを計算
                            const int thumbnailSize = 96;
                            var frameSize = frame.Size;
                            var aspect = (double)frameSize.Width / frameSize.Height;
                            int width, height;
                            
                            if (aspect > 1)
                            {
                                width = thumbnailSize;
                                height = (int)(thumbnailSize / aspect);
                            }
                            else
                            {
                                height = thumbnailSize;
                                width = (int)(thumbnailSize * aspect);
                            }
                            
                            // フレームをリサイズ
                            using var resized = frame.Resize(width, height);
                            if (resized is Bitmap<Bgra8888> bgraFrame)
                            {
                                // Beutl BitmapからAvalonia WriteableBitmapに変換
                                var writeableBitmap = new WriteableBitmap(
                                    new Avalonia.PixelSize(bgraFrame.Width, bgraFrame.Height),
                                    new Avalonia.Vector(96, 96),
                                    Avalonia.Platform.PixelFormat.Bgra8888,
                                    Avalonia.Platform.AlphaFormat.Premul);
                                
                                using (var buf = writeableBitmap.Lock())
                                {
                                    unsafe
                                    {
                                        int size = bgraFrame.ByteCount;
                                        Buffer.MemoryCopy((void*)bgraFrame.Data, (void*)buf.Address, size, size);
                                    }
                                }
                                
                                return writeableBitmap;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 動画デコードエラーは無視
            }
            return null;
        });
    }
    
    public void Dispose()
    {
        _disposables.Dispose();
        Items.Clear();
        TreeItems.Clear();
        SearchResults.Clear();
        
        // サムネイルキャッシュをクリア
        foreach (var bitmap in _thumbnailCache.Values)
        {
            bitmap?.Dispose();
        }
        _thumbnailCache.Clear();
        _thumbnailSemaphore.Dispose();
    }
    
    public void WriteToJson(JsonObject json)
    {
        json["currentPath"] = CurrentPath.Value;
        json["displayMode"] = DisplayMode.Value.ToString();
    }
    
    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("currentPath", out var pathNode) && pathNode is JsonValue pathValue)
        {
            string? path = pathValue.GetValue<string>();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                CurrentPath.Value = path;
            }
        }
        
        if (json.TryGetPropertyValue("displayMode", out var modeNode) && modeNode is JsonValue modeValue)
        {
            string? mode = modeValue.GetValue<string>();
            if (!string.IsNullOrEmpty(mode) && Enum.TryParse<FileExplorerDisplayMode>(mode, out var displayMode))
            {
                DisplayMode.Value = displayMode;
            }
        }
    }
    
    public object? GetService(Type serviceType)
    {
        return null;
    }
    
    public ToolTabExtension Extension => FileExplorerExtension.Instance;
    
    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();
    
    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactiveProperty<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightUpperTop);
    
    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();
    
    public string Header => Strings.FileExplorer;
}