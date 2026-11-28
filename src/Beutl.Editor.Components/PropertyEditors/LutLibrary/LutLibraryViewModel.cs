using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// What the LUT library flyout shows: the files under the library folders, the recent picks above them, and the
// search and favorites filters. Thumbnails are asked for one by one so the list appears before they are ready.
internal sealed class LutLibraryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LutLibraryStore _store;
    private readonly ILutThumbnailSource _thumbnails;
    private readonly Func<bool>? _canApply;
    private readonly List<LutLibraryItemViewModel> _all = [];
    private readonly Dictionary<string, LutLibraryItemViewModel> _byPath =
        new(LutLibraryStore.PathComparer);
    private CancellationTokenSource? _loadCts;
    private string _searchText = string.Empty;
    private bool _favoritesOnly;
    private bool _disposed;

    public LutLibraryViewModel(
        LutLibraryStore store,
        ILutThumbnailSource thumbnails,
        Func<bool>? canApply = null)
    {
        _store = store;
        _thumbnails = thumbnails;
        _canApply = canApply;
        EmptyText = string.Format(Strings.LutLibraryEmpty, store.LibraryFolder);
        RefreshFolders();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LutLibraryItemViewModel>? ItemChosen;

    public ObservableCollection<LutLibraryItemViewModel> Items { get; } = [];

    public ObservableCollection<LutLibraryItemViewModel> RecentItems { get; } = [];

    public ObservableCollection<LutLibraryFolderViewModel> ExtraFolders { get; } = [];

    public string LibraryFolder => _store.LibraryFolder;

    public string EmptyText { get; }

    public bool HasExtraFolders => ExtraFolders.Count > 0;

    public bool HasRecent => RecentItems.Count > 0;

    public bool IsEmpty => _all.Count == 0;

    public bool HasNoMatches => _all.Count > 0 && Items.Count == 0 && RecentItems.Count == 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            string text = value ?? string.Empty;
            if (_searchText == text)
                return;

            _searchText = text;
            Raise();
            Refresh();
        }
    }

    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (_favoritesOnly == value)
                return;

            _favoritesOnly = value;
            Raise();
            Refresh();
        }
    }

    public async Task LoadAsync()
    {
        if (_disposed)
            return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IReadOnlyList<LutLibraryFile> files;
        try
        {
            files = await Task.Run(_store.Scan, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Opening the picker must never fail; the empty state explains what to do.
            return;
        }

        if (_disposed || cts.IsCancellationRequested)
            return;

        Rebuild(files);
        Refresh();
        RefreshFolders();

        LutLibraryItemViewModel[] waiting = _all.Where(item => !item.IsError).ToArray();
        await Task.WhenAll(waiting.Select(item => LoadThumbnailAsync(item, cts.Token)));
    }

    public void ToggleFavorite(LutLibraryItemViewModel item)
    {
        if (_disposed)
            return;

        item.IsFavorite = !item.IsFavorite;
        _store.SetFavorite(item.Path, item.IsFavorite);
        Refresh();
    }

    public void AddFolder(string path)
    {
        if (_disposed || !_store.AddFolder(path))
            return;

        RefreshFolders();
        _ = LoadAsync();
    }

    public void RemoveFolder(string path)
    {
        if (_disposed || !_store.RemoveFolder(path))
            return;

        RefreshFolders();
        _ = LoadAsync();
    }

    public void Choose(LutLibraryItemViewModel item)
    {
        if (_disposed || item.IsError)
            return;

        if (_canApply != null && !_canApply())
            return;

        _store.RecordRecent(item.Path);
        Refresh();
        ItemChosen?.Invoke(this, item);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    private async Task LoadThumbnailAsync(LutLibraryItemViewModel item, CancellationToken cancellationToken)
    {
        LutThumbnailResult result = await _thumbnails.GetAsync(item.Path, cancellationToken);
        if (_disposed || cancellationToken.IsCancellationRequested)
            return;

        if (result.Error != null)
        {
            item.SetError(result.Error);
        }
        else
        {
            item.SetThumbnail(result.Image);
        }
    }

    private void Rebuild(IReadOnlyList<LutLibraryFile> files)
    {
        _all.Clear();
        _byPath.Clear();
        foreach (LutLibraryFile file in files)
        {
            var item = new LutLibraryItemViewModel(file);
            _all.Add(item);
            _byPath[item.Path] = item;
        }
    }

    private void Refresh()
    {
        if (_disposed)
            return;

        string search = _searchText.Trim();
        bool favoritesOnly = _favoritesOnly;

        Items.Clear();
        foreach (LutLibraryItemViewModel item in _all)
        {
            if (Matches(item, search, favoritesOnly))
            {
                Items.Add(item);
            }
        }

        RecentItems.Clear();
        foreach (string path in _store.RecentFiles)
        {
            if (_byPath.TryGetValue(path, out LutLibraryItemViewModel? item)
                && Matches(item, search, favoritesOnly)
                && !RecentItems.Contains(item))
            {
                RecentItems.Add(item);
            }
        }

        Raise(nameof(IsEmpty));
        Raise(nameof(HasNoMatches));
        Raise(nameof(HasRecent));
    }

    private void RefreshFolders()
    {
        ExtraFolders.Clear();
        foreach (string folder in _store.ExtraFolders)
        {
            ExtraFolders.Add(new LutLibraryFolderViewModel(folder));
        }

        Raise(nameof(HasExtraFolders));
    }

    private static bool Matches(LutLibraryItemViewModel item, string search, bool favoritesOnly)
        => (!favoritesOnly || item.IsFavorite)
            && (search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
