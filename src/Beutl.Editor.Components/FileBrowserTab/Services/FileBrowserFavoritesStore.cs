using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

/// <summary>
/// Shared owner of file-browser favorites.
/// </summary>
/// <remarks>
/// Process-wide state is shared by scenes and tabs and accessed on the UI thread.
/// </remarks>
internal sealed class FileBrowserFavoritesStore
{
    private const string PreferenceKey = "FileBrowser.Favorites";

    public static FileBrowserFavoritesStore Instance { get; } = CreateDefault();

    private readonly ILogger _logger = Log.CreateLogger<FileBrowserFavoritesStore>();
    private readonly IPreferences? _preferences;
    private readonly ObservableCollection<string> _favorites = [];
    private int _suspendDepth;
    private bool _pending;

    internal FileBrowserFavoritesStore(IPreferences? preferences)
    {
        _preferences = preferences;
        Load();
        Favorites = new ReadOnlyObservableCollection<string>(_favorites);
        _favorites.CollectionChanged += OnCollectionChanged;
    }

    // Avoid poisoning the static store when preferences cannot be read.
    private static FileBrowserFavoritesStore CreateDefault()
    {
        try
        {
            return new FileBrowserFavoritesStore(Preferences.Default);
        }
        catch (Exception ex)
        {
            Log.CreateLogger<FileBrowserFavoritesStore>()
                .LogWarning(ex, "Failed to resolve preferences; favorites will not persist this session");
            return new FileBrowserFavoritesStore(preferences: null);
        }
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

    // Batch persistence and notifications for multi-path updates.
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

        if (Changed is not { } changed)
            return;

        // A failing tab must not block other listeners.
        foreach (Action handler in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A favorites listener failed to refresh");
            }
        }
    }

    private void Load()
    {
        if (_preferences is null)
            return;

        try
        {
            string json = _preferences.Get(PreferenceKey, "[]");
            string[]? paths = JsonSerializer.Deserialize<string[]>(json);
            if (paths != null)
            {
                // Ignore null/empty entries and deduplicate hand-edited preferences.
                foreach (string path in paths
                             .OfType<string>()
                             .Where(p => !string.IsNullOrEmpty(p))
                             .Distinct(StringComparer.Ordinal))
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
        if (_preferences is null)
            return;

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
