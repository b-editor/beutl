using System.Collections.ObjectModel;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

/// <summary>
/// Per-tab view over shared favorite paths.
/// </summary>
/// <remarks>
/// Favorite items remain per tab because they own expansion and subscription state.
/// </remarks>
internal sealed class FavoritesManager : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<FavoritesManager>();
    private readonly FileBrowserFavoritesStore _store;
    private readonly FileThumbnailService _thumbnails;
    private readonly Action _onStoreChanged;
    private bool _disposed;

    public FavoritesManager(FileThumbnailService thumbnails)
        : this(thumbnails, FileBrowserFavoritesStore.Instance)
    {
    }

    internal FavoritesManager(FileThumbnailService thumbnails, FileBrowserFavoritesStore store)
    {
        _thumbnails = thumbnails;
        _store = store;
        _onStoreChanged = () => Changed?.Invoke();
        _store.Changed += _onStoreChanged;
    }

    public ReadOnlyObservableCollection<string> Favorites => _store.Favorites;

    public ObservableCollection<FileSystemItemViewModel> FavoriteItems { get; } = [];

    public void ToggleFavorite(string currentPath) => _store.Toggle(currentPath);

    public void AddRange(IEnumerable<string> paths) => _store.AddRange(paths);

    public void RefreshFavoriteItems(bool preserveExisting = false)
    {
        // Debounced refreshes may arrive after disposal.
        if (_disposed)
            return;

        if (!preserveExisting)
            FileSystemEnumerator.DisposeAndClear(FavoriteItems);

        var fixedFolders = new[]
        {
            (Path: _thumbnails.Templates.DirectoryPath, Name: Strings.Templates),
            (Path: BeutlEnvironment.GetMaterialsDirectoryPath(), Name: Strings.Materials)
        }.Where(folder => EnsureFixedFolder(folder.Path)).ToArray();

        FileSystemEnumerator.RefreshCollection(_thumbnails, FavoriteItems,
            fixedFolders.Select(folder => (folder.Path, true)).Concat(EnumerateFavorites()));

        for (int i = 0; i < fixedFolders.Length; i++)
            FavoriteItems[i].Name.Value = fixedFolders[i].Name;
    }

    private IEnumerable<(string Path, bool IsDirectory)> EnumerateFavorites()
    {
        foreach (string path in Favorites)
        {
            if (Directory.Exists(path))
                yield return (path, true);
            else if (File.Exists(path))
                yield return (path, false);
        }
    }

    // 共有のお気に入りが変わったときに発火する。
    public event Action? Changed;

    // Isolate tab refresh failures so one tab cannot block the others.
    private bool EnsureFixedFolder(string directory)
    {
        if (Directory.Exists(directory))
            return true;

        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create the favorites folder {Directory}", directory);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        // Unsubscribe because the store outlives each tab.
        _store.Changed -= _onStoreChanged;
        Changed = null;
        FileSystemEnumerator.DisposeAndClear(FavoriteItems);
    }
}
