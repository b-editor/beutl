using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

/// <summary>
/// The single owner of the file browser's favorite directories.
/// </summary>
/// <remarks>
/// File browsers exist per scene and per tab, so a per-tab list would let whichever instance saves
/// last overwrite every other instance's edits. Deliberately not <see cref="IDisposable"/>: closing
/// one tab must not be able to tear down process-wide state. Touch only from the UI thread.
/// </remarks>
internal sealed class FileBrowserFavoritesStore
{
    private const string PreferenceKey = "FileBrowser.Favorites";

    public static FileBrowserFavoritesStore Instance { get; } = new(Preferences.Default);

    private readonly ILogger _logger = Log.CreateLogger<FileBrowserFavoritesStore>();
    private readonly IPreferences _preferences;
    private readonly ObservableCollection<string> _favorites = [];
    private int _suspendDepth;
    private bool _pending;

    // Test seam: points the store at an in-memory preference set instead of the ambient BEUTL_HOME.
    internal FileBrowserFavoritesStore(IPreferences preferences)
    {
        _preferences = preferences;
        Load();
        Favorites = new ReadOnlyObservableCollection<string>(_favorites);
        _favorites.CollectionChanged += OnCollectionChanged;
    }

    public ReadOnlyObservableCollection<string> Favorites { get; }

    public event Action? Changed;

    public bool Contains(string path) => _favorites.Contains(path);

    public void Toggle(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!_favorites.Remove(path))
        {
            _favorites.Add(path);
        }
    }

    public void Remove(string path) => _favorites.Remove(path);

    // IPreferences.Set rewrites the whole preferences file, and every Changed listener rebuilds its
    // item list, so a multi-path drop must not go through Add one path at a time.
    public void AddRange(IEnumerable<string> paths)
    {
        _suspendDepth++;
        try
        {
            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path) && !_favorites.Contains(path))
                {
                    _favorites.Add(path);
                }
            }
        }
        finally
        {
            _suspendDepth--;
            if (_suspendDepth == 0 && _pending)
            {
                _pending = false;
                Flush();
            }
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suspendDepth > 0)
        {
            _pending = true;
            return;
        }

        Flush();
    }

    private void Flush()
    {
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            string json = _preferences.Get(PreferenceKey, "[]");
            string[]? paths = JsonSerializer.Deserialize<string[]>(json);
            if (paths != null)
            {
                foreach (string path in paths)
                {
                    _favorites.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load favorites from preferences");
        }
    }

    private void Save()
    {
        try
        {
            _preferences.Set(PreferenceKey, JsonSerializer.Serialize(_favorites.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save favorites to preferences");
        }
    }
}
