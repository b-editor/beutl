using System.Collections.Concurrent;
using System.Text.Json;
using Beutl.Graphics;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// The .cube files a LUT property can pick from, with the extra folders and pinned favorites kept in library.json
// beside the files. One store is shared per library folder, so editors cannot overwrite each other's settings.
internal sealed class LutLibraryStore
{
    private const string SettingsFileName = "library.json";

    private static readonly StringComparer s_pathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly ConcurrentDictionary<string, LutLibraryStore> s_shared = new(s_pathComparer);

    private readonly List<string> _extraFolders = [];
    private readonly HashSet<string> _favorites = new(s_pathComparer);
    private readonly Dictionary<(string Path, long Modified), string?> _validation = [];
    private readonly object _gate = new();
    private readonly object _validationGate = new();

    private LutLibraryStore(string libraryFolder)
    {
        LibraryFolder = Path.GetFullPath(libraryFolder);
        Load();
    }

    public static LutLibraryStore GetShared(string libraryFolder)
    {
        string fullPath = Path.GetFullPath(libraryFolder);
        return s_shared.GetOrAdd(fullPath, static path => new LutLibraryStore(path));
    }

    public string LibraryFolder { get; }

    public string SettingsPath => Path.Combine(LibraryFolder, SettingsFileName);

    // The comparer the store treats paths with, so pickers resolve the same file the same way.
    public static StringComparer PathComparer => s_pathComparer;

    public IReadOnlyList<string> ExtraFolders
    {
        get
        {
            lock (_gate)
            {
                return [.. _extraFolders];
            }
        }
    }

    public void EnsureLibraryFolder()
    {
        try
        {
            Directory.CreateDirectory(LibraryFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A read-only or invalid home must not stop the picker from opening.
        }
    }

    // Lists the .cube files in the library folder and the added folders, reading each once to tell a usable LUT
    // from one the picker should show as an error.
    public IReadOnlyList<LutLibraryFile> Scan()
    {
        EnsureLibraryFolder();

        string[] folders;
        HashSet<string> favorites;
        lock (_gate)
        {
            folders = [LibraryFolder, .. _extraFolders];
            favorites = new HashSet<string>(_favorites, s_pathComparer);
        }

        var paths = new List<string>();
        var seen = new HashSet<string>(s_pathComparer);
        foreach (string folder in folders)
        {
            CollectFiles(folder, paths, seen);
        }

        var files = new List<LutLibraryFile>(paths.Count);
        foreach (string path in paths)
        {
            files.Add(new LutLibraryFile(path, Path.GetFileName(path), favorites.Contains(path), Validate(path)));
        }

        files.Sort(static (x, y) =>
        {
            int byName = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(x.Path, y.Path);
        });

        return files;
    }

    public bool AddFolder(string path)
    {
        string? full = NormalizeOrNull(path);
        if (full is null || !Directory.Exists(full))
            return false;

        lock (_gate)
        {
            if (s_pathComparer.Equals(full, LibraryFolder)
                || _extraFolders.Any(folder => s_pathComparer.Equals(folder, full)))
            {
                return false;
            }

            _extraFolders.Add(full);
        }

        Save();
        return true;
    }

    public bool RemoveFolder(string path)
    {
        string? full = NormalizeOrNull(path);
        if (full is null)
            return false;

        bool removed;
        lock (_gate)
        {
            removed = _extraFolders.RemoveAll(folder => s_pathComparer.Equals(folder, full)) > 0;
        }

        if (removed)
            Save();
        return removed;
    }

    public bool IsFavorite(string path)
    {
        string? full = NormalizeOrNull(path);
        if (full is null)
            return false;

        lock (_gate)
        {
            return _favorites.Contains(full);
        }
    }

    public void SetFavorite(string path, bool favorite)
    {
        string? full = NormalizeOrNull(path);
        if (full is null)
            return;

        bool changed;
        lock (_gate)
        {
            changed = favorite ? _favorites.Add(full) : _favorites.Remove(full);
        }

        if (changed)
            Save();
    }

    private static void CollectFiles(string folder, List<string> paths, HashSet<string> seen)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;

            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (!string.Equals(Path.GetExtension(file), ".cube", StringComparison.OrdinalIgnoreCase))
                    continue;

                string full = Path.GetFullPath(file);
                if (seen.Add(full))
                {
                    paths.Add(full);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A folder that disappears or cannot be read simply contributes nothing.
        }
    }

    // The error a file reports when read as a LUT, or null when it is usable. The last read per write time is
    // kept because opening the picker again should not parse every LUT a second time.
    private string? Validate(string path)
    {
        long modified;
        try
        {
            modified = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception e)
        {
            return e.Message;
        }

        lock (_validationGate)
        {
            if (_validation.TryGetValue((path, modified), out string? cached))
                return cached;
        }

        string? error = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            CubeFile.FromStream(stream);
        }
        catch (Exception e)
        {
            error = e.Message;
        }

        lock (_validationGate)
        {
            if (_validation.Count > 256)
            {
                _validation.Clear();
            }

            _validation[(path, modified)] = error;
        }

        return error;
    }

    private static string? NormalizeOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return;

            string json = File.ReadAllText(SettingsPath);
            LibrarySettings? settings = JsonSerializer.Deserialize<LibrarySettings>(json, s_jsonOptions);
            if (settings is null)
                return;

            lock (_gate)
            {
                foreach (string folder in settings.ExtraFolders ?? [])
                {
                    if (NormalizeOrNull(folder) is { } full
                        && !s_pathComparer.Equals(full, LibraryFolder)
                        && !_extraFolders.Any(existing => s_pathComparer.Equals(existing, full)))
                    {
                        _extraFolders.Add(full);
                    }
                }

                foreach (string favorite in settings.Favorites ?? [])
                {
                    if (NormalizeOrNull(favorite) is { } full)
                    {
                        _favorites.Add(full);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            // A corrupt or unreadable file starts an empty library instead of failing the picker.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(LibraryFolder);

            LibrarySettings settings;
            lock (_gate)
            {
                settings = new LibrarySettings
                {
                    ExtraFolders = [.. _extraFolders],
                    Favorites = [.. _favorites]
                };
            }

            string json = JsonSerializer.Serialize(settings, s_jsonOptions);
            string temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Losing the library settings is better than failing the edit that changed them.
        }
    }

    private sealed class LibrarySettings
    {
        public List<string>? ExtraFolders { get; set; }

        public List<string>? Favorites { get; set; }
    }
}
