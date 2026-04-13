using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class ObjectTemplateService
{
    public static readonly ObjectTemplateService Instance = new();
    private readonly CoreList<ObjectTemplateItem> _items = [];

    private readonly string _directoryPath = Path.Combine(
        BeutlEnvironment.GetHomeDirectoryPath(), "templates");

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

    public ObjectTemplateItem? AddFromInstance(ICoreSerializable instance, string name)
    {
        lock (_lock)
        {
            string uniqueName = GetUniqueNameLocked(name);
            var item = ObjectTemplateItem.CreateFromInstance(instance, uniqueName);
            if (!SaveItemToFile(item))
            {
                return null;
            }

            _items.Add(item);
            _logger.LogInformation("Added new ObjectTemplateItem: {Name}", uniqueName);
            return item;
        }
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
                DateTime diskTime = GetLastWriteTimeOrDefault(filePath);
                if (diskTime != default && diskTime <= cached.LastWriteTimeUtc)
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

                foreach (string filePath in Directory.EnumerateFiles(
                             _directoryPath, "*.json", SearchOption.AllDirectories))
                {
                    var item = LoadFromFile(filePath);
                    if (item != null)
                    {
                        _items.Add(item);
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
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                Filter = "*.json",
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;

            _logger.LogInformation("Started watching templates directory: {DirectoryPath}", _directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", _directoryPath);
        }
    }

    private ObjectTemplateItem? FindByFilePathLocked(string filePath)
    {
        foreach (ObjectTemplateItem item in _items)
        {
            if (string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

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

    private void RefreshFromFileSystem()
    {
        try
        {
            if (!Directory.Exists(_directoryPath)) return;

            var diskFiles = new HashSet<string>(
                Directory.EnumerateFiles(_directoryPath, "*.json", SearchOption.AllDirectories),
                StringComparer.OrdinalIgnoreCase);

            lock (_lock)
            {
                // 削除されたファイルに対応するアイテムを削除
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    ObjectTemplateItem item = _items[i];
                    if (item.FilePath == null || !diskFiles.Contains(item.FilePath))
                    {
                        _items.RemoveAt(i);
                        _logger.LogInformation("Removed template (file gone): {FilePath}", item.FilePath);
                    }
                }

                // 外部変更を検知したら再読み込み、既読パスを収集
                var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _items.Count; i++)
                {
                    ObjectTemplateItem item = _items[i];
                    if (item.FilePath == null) continue;

                    loadedPaths.Add(item.FilePath);

                    DateTime diskTime = GetLastWriteTimeOrDefault(item.FilePath);
                    if (diskTime == default || diskTime <= item.LastWriteTimeUtc)
                        continue;

                    ObjectTemplateItem? reloaded = LoadFromFile(item.FilePath);
                    if (reloaded != null)
                    {
                        _items[i] = reloaded;
                        _logger.LogInformation("Reloaded template (file changed): {FilePath}", item.FilePath);
                    }
                }

                // 新しいファイルを読み込んで追加
                foreach (string filePath in diskFiles)
                {
                    if (loadedPaths.Contains(filePath)) continue;

                    ObjectTemplateItem? newItem = LoadFromFile(filePath);
                    if (newItem != null)
                    {
                        _items.Add(newItem);
                        _logger.LogInformation("Added template (new file): {FilePath}", filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh templates from filesystem.");
        }
    }
}
