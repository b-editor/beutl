using System.Reactive.Linq;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// Feeds the library item picker with the LUT files, where a pin is a favorite. The file list loads in the
// background; thumbnails are asked for only by the tiles that show them.
internal sealed class LutLibraryPickerViewModel : ILibraryItemPickerViewModel, ILutLibraryFolderPicker, IDisposable
{
    private static readonly TimeSpan s_searchDelay = TimeSpan.FromMilliseconds(100);
    private static readonly string[] s_noCategories = [];

    private readonly LutLibraryStore _store;
    private readonly ILutThumbnailSource _thumbnails;
    private readonly Func<Task<string?>>? _chooseFolder;
    private readonly CompositeDisposable _disposables = [];
    private readonly Dictionary<string, LutLibraryThumbnailSource> _sources = new(LutLibraryStore.PathComparer);
    private readonly CancellationTokenSource _thumbnailCancellation = new();
    private readonly Func<IReadOnlyList<LutLibraryFile>> _scan;
    private IReadOnlyList<LutLibraryFile> _files = [];
    private string _appliedSearchText = string.Empty;
    private string? _highlightedPath;
    private int _loadGeneration;
    private bool _updating;
    private bool _disposed;

    public LutLibraryPickerViewModel(
        LutLibraryStore store,
        ILutThumbnailSource thumbnails,
        Func<Task<string?>>? chooseFolder = null)
        : this(store, thumbnails, chooseFolder, scan: null)
    {
    }

    // Tests pass a scan of their own to finish scans out of order.
    internal LutLibraryPickerViewModel(
        LutLibraryStore store,
        ILutThumbnailSource thumbnails,
        Func<Task<string?>>? chooseFolder,
        Func<IReadOnlyList<LutLibraryFile>>? scan)
    {
        _store = store;
        _thumbnails = thumbnails;
        _chooseFolder = chooseFolder;
        _scan = scan ?? store.Scan;
        ShowTiles.Value = Preferences.Default.Get(SelectLibraryItemDialogViewModel.ShowTilesKey, true);
        SearchText.Skip(1)
            .Throttle(s_searchDelay)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => FlushSearch())
            .DisposeWith(_disposables);
        ShowTiles.Skip(1)
            .Subscribe(value => Preferences.Default.Set(SelectLibraryItemDialogViewModel.ShowTilesKey, value))
            .DisposeWith(_disposables);
        SelectedItem.Subscribe(item =>
        {
            if (!_updating && item?.UserData is LutLibraryPickerItem entry)
            {
                _highlightedPath = entry.Path;
            }
        }).DisposeWith(_disposables);
    }

    public ReactiveCollection<PinnableLibraryItem> Items { get; } = [];

    public ReactiveProperty<string?> SearchText { get; } = new();

    public ReactiveProperty<PinnableLibraryItem?> SelectedItem { get; } = new();

    public IReadOnlyList<string> Categories => s_noCategories;

    public ReactiveProperty<int> SelectedCategoryIndex { get; } = new();

    public ReactiveProperty<bool> ShowAll { get; } = new();

    public bool CanShowAll => false;

    public bool HasReferencesTab => false;

    public bool ShowsOnlyReferences => false;

    public ReactiveCollection<PinnableLibraryItem> ReferenceItems { get; } = [];

    public bool HasThumbnails => true;

    public ReactiveProperty<bool> ShowTiles { get; } = new();

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public async Task LoadAsync()
    {
        if (_disposed)
            return;

        // A folder added while an earlier scan runs starts a newer one; only the newest scan's files may land,
        // whichever finishes last.
        int generation = ++_loadGeneration;
        IsBusy.Value = true;
        try
        {
            IReadOnlyList<LutLibraryFile> files = await Task.Run(_scan);
            if (_disposed || generation != _loadGeneration)
                return;

            _files = files;
            UpdateItems();
        }
        catch (Exception)
        {
            // The picker opens empty rather than failing when a folder cannot be read.
        }
        finally
        {
            if (!_disposed && generation == _loadGeneration)
                IsBusy.Value = false;
        }
    }

    public void FlushSearch()
    {
        if (_disposed)
            return;

        string text = SearchText.Value ?? string.Empty;
        if (string.Equals(text, _appliedSearchText, StringComparison.Ordinal))
            return;

        _appliedSearchText = text;
        UpdateItems();
    }

    public void Pin(PinnableLibraryItem item) => SetPinned(item, pinned: true);

    public void Unpin(PinnableLibraryItem item) => SetPinned(item, pinned: false);

    public async Task AddFolderAsync()
    {
        if (_disposed || _chooseFolder is null)
            return;

        string? path;
        try
        {
            path = await _chooseFolder();
        }
        catch (Exception)
        {
            return;
        }

        if (path is null || !_store.AddFolder(path))
            return;

        // Rescan in place so the files under the new folder appear in the open picker.
        await LoadAsync();
    }

    // Closing the picker cancels thumbnail renders still waiting for a slot; started ones finish into the cache.
    public void CancelThumbnailWork()
    {
        if (!_thumbnailCancellation.IsCancellationRequested)
        {
            _thumbnailCancellation.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelThumbnailWork();
        _disposables.Dispose();
    }

    private void SetPinned(PinnableLibraryItem item, bool pinned)
    {
        if (_disposed || item.UserData is not LutLibraryPickerItem entry)
            return;

        _store.SetFavorite(entry.Path, pinned);
        _files = _files
            .Select(file => LutLibraryStore.PathComparer.Equals(file.Path, entry.Path)
                ? file with { IsFavorite = pinned }
                : file)
            .ToArray();
        UpdateItems();
    }

    private void UpdateItems()
    {
        string? selectedPath = _highlightedPath;
        LutLibraryFile[] matches = _files
            .Where(file => Matches(file, _appliedSearchText))
            .OrderByDescending(file => file.IsFavorite)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _updating = true;
        try
        {
            Items.Clear();
            foreach (LutLibraryFile file in matches)
            {
                if (!_sources.TryGetValue(file.Path, out LutLibraryThumbnailSource? source))
                {
                    source = new LutLibraryThumbnailSource(
                        file.Path, _thumbnails, _thumbnailCancellation.Token);
                    _sources[file.Path] = source;
                }

                string description = file.IsError
                    ? Strings.LutLibraryInvalidFile
                    : Path.GetDirectoryName(file.Path) ?? string.Empty;
                Items.Add(new PinnableLibraryItem(
                    Path.GetFileNameWithoutExtension(file.Path),
                    file.IsFavorite,
                    new LutLibraryPickerItem(file.Path, file.IsError),
                    description)
                {
                    Thumbnail = file.IsError ? null : source
                });
            }
        }
        finally
        {
            _updating = false;
        }

        SelectedItem.Value = selectedPath != null
            ? Items.FirstOrDefault(item => item.UserData is LutLibraryPickerItem entry
                && LutLibraryStore.PathComparer.Equals(entry.Path, selectedPath))
            : null;

        // A search confirms from what is shown even after the list was rebuilt under the typed text.
        if (SelectedItem.Value == null && !string.IsNullOrWhiteSpace(_appliedSearchText))
        {
            SelectedItem.Value = Items.FirstOrDefault();
        }
    }

    private static bool Matches(LutLibraryFile file, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        string haystack = $"{file.Name} {Path.GetDirectoryName(file.Path)}";
        return search
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => haystack.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }
}
