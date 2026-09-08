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
    private readonly Action _onStoreChanged;
    private bool _disposed;

    public FavoritesManager()
        : this(FileBrowserFavoritesStore.Instance)
    {
    }

    internal FavoritesManager(FileBrowserFavoritesStore store)
    {
        _store = store;
        _onStoreChanged = () => Changed?.Invoke();
        _store.Changed += _onStoreChanged;
    }

    public ReadOnlyObservableCollection<string> Favorites => _store.Favorites;

    public ObservableCollection<FileSystemItemViewModel> FavoriteItems { get; } = [];

    public void ToggleFavorite(string currentPath) => _store.Toggle(currentPath);

    public void AddRange(IEnumerable<string> paths) => _store.AddRange(paths);

    public void RefreshFavoriteItems()
    {
        // Debounced refreshes may arrive after disposal.
        if (_disposed)
            return;

        DisposeAndClearItems();

        // テンプレートフォルダを常に先頭に表示（ローカライズ名で）
        AddFixedFolder(ObjectTemplateService.Instance.DirectoryPath, Strings.Templates);

        // 素材フォルダも常に表示（ローカライズ名で）
        AddFixedFolder(BeutlEnvironment.GetMaterialsDirectoryPath(), Strings.Materials);

        foreach (string path in Favorites)
        {
            if (Directory.Exists(path))
            {
                FavoriteItems.Add(new FileSystemItemViewModel(path, true));
            }
            else if (File.Exists(path))
            {
                FavoriteItems.Add(new FileSystemItemViewModel(path, false));
            }
        }
    }

    // お気に入りコレクション変更時のコールバック。ホームビュー表示中の場合にアイテムを更新する。
    public event Action? Changed;

    // Isolate tab refresh failures so one tab cannot block the others.
    private void AddFixedFolder(string directory, string localizedName)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create the favorites folder {Directory}", directory);
            return;
        }

        var item = new FileSystemItemViewModel(directory, true);
        item.Name.Value = localizedName;
        FavoriteItems.Add(item);
    }

    private void DisposeAndClearItems()
    {
        foreach (var item in FavoriteItems)
        {
            item.Dispose();
        }

        FavoriteItems.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        // Unsubscribe because the store outlives each tab.
        _store.Changed -= _onStoreChanged;
        Changed = null;
        DisposeAndClearItems();
    }
}
