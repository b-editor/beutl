using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed partial class ObjectTemplateService
{
    public static readonly ObjectTemplateService Instance = new();
    private readonly CoreList<ObjectTemplateItem> _items = [];

    private readonly string _directoryPath = BeutlEnvironment.GetTemplatesDirectoryPath();

    private static readonly TimeSpan s_debounceInterval = TimeSpan.FromMilliseconds(300);

    private readonly ILogger _logger = Log.CreateLogger<ObjectTemplateService>();
    private readonly Lock _lock = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;

    private ObjectTemplateService()
    {
        RestoreItems();
        StartWatching();
    }

    public string DirectoryPath => _directoryPath;

    /// <summary>
    /// Saves <paramref name="instance"/> as a template, embedding a rendered preview when one can be produced.
    /// </summary>
    /// <remarks>
    /// Rendering hops to the render thread, so it runs before the lock is taken — awaiting while
    /// holding the lock would deadlock against a render thread that needs it back, which is also
    /// why the name is resolved in the same lock window as the write. Persistence is pushed onto
    /// the thread pool because the render dispatcher completes its task on the render thread, and a
    /// caller with no synchronization context would otherwise write the file there.
    /// </remarks>
    public async ValueTask<ObjectTemplateItem?> AddFromInstanceAsync(
        ICoreSerializable instance, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\'))
            return null;
        ObjectTemplateItem item;
        try
        {
            item = ObjectTemplateItem.CreateFromInstance(instance, name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to serialize template {Name}.", name);
            return null;
        }
        item.Preview = await ObjectTemplatePreviewRenderer.RenderPngAsync(instance, cancellationToken);

        return await Task.Run(
            () =>
            {
                lock (_lock)
                {
                    item.Name.Value = GetUniqueNameLocked(name);
                    if (!SaveItemToFile(item))
                    {
                        return null;
                    }

                    _items.Add(item);
                    _logger.LogInformation("Added new ObjectTemplateItem: {Name}", item.Name.Value);
                    return item;
                }
            },
            cancellationToken);
    }

    public string GetUniqueName(string baseName)
    {
        lock (_lock)
        {
            return GetUniqueNameLocked(baseName);
        }
    }

    private string GetUniqueNameLocked(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Template";

        string candidate = baseName;
        int counter = 2;
        while (NameExistsLocked(candidate))
        {
            candidate = $"{baseName} ({counter})";
            counter++;
        }

        return candidate;
    }

    private bool NameExistsLocked(string name)
    {
        string filePath = Path.Combine(_directoryPath, name + ".json");
        if (File.Exists(filePath)) return true;

        // Display names remain case-insensitively unique independently of path identity.
        foreach (ObjectTemplateItem item in _items)
        {
            if (string.Equals(item.Name.Value, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public ObjectTemplateItem? FindById(Guid id)
    {
        lock (_lock)
        {
            foreach (ObjectTemplateItem item in _items)
            {
                if (item.Id == id) return item;
            }
        }

        return null;
    }

    public IEnumerable<ObjectTemplateItem> FindByBaseType(Type baseType)
    {
        lock (_lock)
        {
            var result = new List<ObjectTemplateItem>();
            foreach (ObjectTemplateItem item in _items)
            {
                if (item.BaseType == baseType)
                    result.Add(item);
            }
            return result;
        }
    }

    public ObjectTemplateItem? TryLoadFromFile(string filePath)
    {
        lock (_lock)
        {
            ObjectTemplateItem? cached = FindByFilePathLocked(filePath);
            if (cached != null)
            {
                string? canonicalPath = ResolveCanonicalPathOrNull(filePath);
                DateTime diskTime = GetLastWriteTimeOrDefault(filePath);
                if (string.Equals(
                        canonicalPath,
                        cached.CanonicalFilePath,
                        StringComparison.Ordinal)
                    && diskTime != default
                    && diskTime <= cached.LastWriteTimeUtc)
                {
                    return cached;
                }

                // 外部変更されているので再読み込み
                ObjectTemplateItem? reloaded = LoadFromFile(filePath);
                if (reloaded != null)
                {
                    int index = _items.IndexOf(cached);
                    if (index >= 0)
                    {
                        _items[index] = reloaded;
                    }

                    return reloaded;
                }

                return cached;
            }
        }

        return LoadFromFile(filePath);
    }

    private ObjectTemplateItem? LoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            string name = Path.GetFileNameWithoutExtension(filePath);
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(filePath);
            using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode == null) return null;

            ObjectTemplateItem? item = ObjectTemplateItem.FromJson(jsonNode, name, filePath, _logger);
            if (item != null)
            {
                item.LastWriteTimeUtc = lastWriteTime;
                item.CanonicalFilePath = ResolveCanonicalPathOrNull(filePath);
            }

            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load template from file: {FilePath}", filePath);
            return null;
        }
    }

    private static DateTime GetLastWriteTimeOrDefault(string filePath)
    {
        try
        {
            return File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : default;
        }
        catch
        {
            return default;
        }
    }

    private bool SaveItemToFile(ObjectTemplateItem item)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            string filePath = Path.Combine(_directoryPath, item.Name.Value + ".json");
            JsonNode json = ObjectTemplateItem.ToJson(item);

            using (FileStream stream = File.Create(filePath))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                json.WriteTo(writer);
            }

            item.FilePath = filePath;
            item.LastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
            item.CanonicalFilePath = ResolveCanonicalPathOrNull(filePath);
            _logger.LogInformation("Saved ObjectTemplateItem to file: {FilePath}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save ObjectTemplateItem: {Name}", item.Name.Value);
            return false;
        }
    }

    public void RestoreItems()
    {
        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                _logger.LogInformation("Templates directory not found: {DirectoryPath}", _directoryPath);
                return;
            }

            lock (_lock)
            {
                _items.Clear();
                string[] diskFiles = Directory.EnumerateFiles(
                        _directoryPath,
                        "*.json",
                        SearchOption.AllDirectories)
                    .ToArray();
                CanonicalPathSet diskPaths = CanonicalPathSet.FromEnumeratedFiles(diskFiles);
                var loadedPaths = new CanonicalPathSet();
                foreach (string filePath in diskPaths.GetPreferredPaths(diskFiles))
                {
                    diskPaths.TryGetExact(filePath, out string? canonicalPath);
                    if (loadedPaths.ContainsKnown(filePath, canonicalPath)) continue;

                    var item = LoadFromFile(filePath);
                    if (item != null)
                    {
                        _items.Add(item);
                        loadedPaths.AddKnown(filePath, canonicalPath);
                    }
                }

                _logger.LogInformation("Restored {Count} ObjectTemplateItem from directory: {DirectoryPath}",
                    _items.Count, _directoryPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring templates.");
        }
    }

    private void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            _watcher = new FileSystemWatcher(_directoryPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                Filter = "*",
                IncludeSubdirectories = true
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
            _watcher.EnableRaisingEvents = true;

            _logger.LogInformation("Started watching templates directory: {DirectoryPath}", _directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", _directoryPath);
        }
    }

    private ObjectTemplateItem? FindByFilePathLocked(string filePath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (IsPathResolutionFailure(ex))
        {
            return null;
        }

        string? canonicalPath = ResolveCanonicalPathOrNull(fullPath);
        foreach (ObjectTemplateItem item in _items)
        {
            if (item.FilePath is not { } itemFilePath)
            {
                continue;
            }

            if (string.Equals(Path.GetFullPath(itemFilePath), fullPath, StringComparison.Ordinal)
                || canonicalPath is not null
                && string.Equals(
                    item.CanonicalFilePath,
                    canonicalPath,
                    StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private static string? ResolveCanonicalPathOrNull(string path)
    {
        try
        {
            return FilePathComparison.ResolveCanonicalPath(path);
        }
        catch (Exception ex) when (IsPathResolutionFailure(ex))
        {
            return null;
        }
    }

    // The path APIs report malformed, inaccessible or unsupported paths through these
    // exception types; any other exception is a real fault and must keep propagating.
    private static bool IsPathResolutionFailure(Exception ex)
        => ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;

            Task.Delay(s_debounceInterval, token).ContinueWith(_ =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        RefreshFromFileSystem();
                    }
                },
                token,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }
    }

    internal void RefreshFromFileSystem()
    {
        try
        {
            if (!Directory.Exists(_directoryPath)) return;

            string[] diskFiles = Directory
                .EnumerateFiles(_directoryPath, "*.json", SearchOption.AllDirectories)
                .ToArray();
            CanonicalPathSet diskPaths = CanonicalPathSet.FromEnumeratedFiles(diskFiles);
            IEnumerable<string> preferredDiskFiles = diskPaths.GetPreferredPaths(diskFiles);

            lock (_lock)
            {
                RemoveItemsWithoutFileLocked(diskPaths);
                CanonicalPathSet loadedPaths = ReloadChangedItemsLocked(diskPaths);
                AddNewFilesLocked(preferredDiskFiles, diskPaths, loadedPaths);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh templates from filesystem.");
        }
    }

    private void RemoveItemsWithoutFileLocked(CanonicalPathSet diskPaths)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            ObjectTemplateItem item = _items[i];
            if (item.FilePath == null
                || !diskPaths.TryMatch(item.FilePath, out _))
            {
                _items.RemoveAt(i);
                _logger.LogInformation("Removed template (file gone): {FilePath}", item.FilePath);
                continue;
            }

        }
    }

    // Returns the identities that stay loaded, so new-file discovery skips their aliases.
    private CanonicalPathSet ReloadChangedItemsLocked(CanonicalPathSet diskPaths)
    {
        var loadedPaths = new CanonicalPathSet();
        for (int i = 0; i < _items.Count; i++)
        {
            ObjectTemplateItem item = _items[i];
            if (item.FilePath is not { } filePath) continue;

            diskPaths.TryMatch(filePath, out string? currentCanonicalPath);
            if (currentCanonicalPath is not null
                && diskPaths.TryGetPreferredPath(
                    currentCanonicalPath,
                    out string? preferredPath)
                && preferredPath is not null
                && !string.Equals(
                    filePath,
                    preferredPath,
                    StringComparison.Ordinal))
            {
                ObjectTemplateItem? preferred = LoadFromFile(preferredPath);
                if (preferred is not null)
                {
                    _items[i] = item = preferred;
                    filePath = preferredPath;
                    currentCanonicalPath = preferred.CanonicalFilePath;
                    _logger.LogInformation(
                        "Reloaded template from preferred path: {FilePath}",
                        preferredPath);
                }
            }

            if (loadedPaths.ContainsKnown(filePath, currentCanonicalPath))
            {
                _items.RemoveAt(i--);
                _logger.LogInformation(
                    "Removed duplicate template identity: {FilePath}",
                    filePath);
                continue;
            }

            loadedPaths.AddKnown(filePath, currentCanonicalPath);

            DateTime diskTime = GetLastWriteTimeOrDefault(filePath);
            bool targetChanged = !string.Equals(
                currentCanonicalPath,
                item.CanonicalFilePath,
                StringComparison.Ordinal);
            if (!targetChanged
                && (diskTime == default || diskTime <= item.LastWriteTimeUtc))
                continue;

            ObjectTemplateItem? reloaded = LoadFromFile(filePath);
            if (reloaded != null)
            {
                _items[i] = reloaded;
                _logger.LogInformation("Reloaded template (file changed): {FilePath}", filePath);
            }
        }

        return loadedPaths;
    }

    private void AddNewFilesLocked(
        IEnumerable<string> preferredDiskFiles, CanonicalPathSet diskPaths, CanonicalPathSet loadedPaths)
    {
        foreach (string filePath in preferredDiskFiles)
        {
            diskPaths.TryGetExact(filePath, out string? canonicalPath);
            if (loadedPaths.ContainsKnown(filePath, canonicalPath)) continue;

            ObjectTemplateItem? newItem = LoadFromFile(filePath);
            if (newItem != null)
            {
                _items.Add(newItem);
                loadedPaths.AddKnown(filePath, canonicalPath);
                _logger.LogInformation("Added template (new file): {FilePath}", filePath);
            }
        }
    }
}
