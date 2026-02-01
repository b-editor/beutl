using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

// ファイルブラウザのお気に入りディレクトリを管理する。
// Preferencesへの永続化とホームビュー用のアイテム生成を担当。
internal sealed class FavoritesManager : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<FavoritesManager>();

    public FavoritesManager()
    {
        LoadFavorites();
        Favorites.CollectionChanged += OnFavoritesChanged;
    }

    public ObservableCollection<string> Favorites { get; } = [];

    public ObservableCollection<FileSystemItemViewModel> FavoriteItems { get; } = [];

    public void ToggleFavorite(string currentPath)
    {
        if (string.IsNullOrEmpty(currentPath))
            return;

        if (Favorites.Contains(currentPath))
        {
            Favorites.Remove(currentPath);
        }
        else
        {
            Favorites.Add(currentPath);
        }
    }

    public void RemoveFavorite(string path)
    {
        Favorites.Remove(path);
    }

    public void RefreshFavoriteItems()
    {
        DisposeAndClearItems();
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

    private void OnFavoritesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SaveFavorites();
        Changed?.Invoke();
    }

    private void LoadFavorites()
    {
        try
        {
            string json = Preferences.Default.Get("FileBrowser.Favorites", "[]");
            var paths = JsonSerializer.Deserialize<string[]>(json);
            if (paths != null)
            {
                foreach (var p in paths)
                {
                    Favorites.Add(p);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load favorites from preferences");
        }
    }

    private void SaveFavorites()
    {
        try
        {
            Preferences.Default.Set("FileBrowser.Favorites", JsonSerializer.Serialize(Favorites.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save favorites to preferences");
        }
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
        Favorites.CollectionChanged -= OnFavoritesChanged;
        DisposeAndClearItems();
    }
}
