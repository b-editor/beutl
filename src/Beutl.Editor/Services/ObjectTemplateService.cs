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

    public ObjectTemplateItem AddFromInstance(ICoreSerializable instance, string name)
    {
        var item = ObjectTemplateItem.CreateFromInstance(instance, name);
        lock (_lock)
        {
            SaveItemToFile(item);
            _items.Add(item);
        }
        _logger.LogInformation("Added new ObjectTemplateItem: {Name}", name);
        return item;
    }

    public void Remove(ObjectTemplateItem item)
    {
        lock (_lock)
        {
            _items.Remove(item);
            DeleteItemFile(item);
        }
        _logger.LogInformation("Removed ObjectTemplateItem: {Name}", item.Name.Value);
    }

    public void Rename(ObjectTemplateItem item, string newName)
    {
        string oldName = item.Name.Value;
        lock (_lock)
        {
            string? oldPath = item.FilePath;

            if (oldPath != null && File.Exists(oldPath))
            {
                try
                {
                    string directory = Path.GetDirectoryName(oldPath)!;
                    string newPath = Path.Combine(directory, newName + ".json");
                    File.Move(oldPath, newPath);
                    item.FilePath = newPath;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to rename template file: {OldName} -> {NewName}", oldName, newName);
                }
            }

            item.Name.Value = newName;
        }
        _logger.LogInformation("Renamed ObjectTemplateItem from {OldName} to {NewName}", oldName, newName);
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

    public IEnumerable<ObjectTemplateItem> FindByCategory(string categoryFormat)
    {
        lock (_lock)
        {
            var result = new List<ObjectTemplateItem>();
            foreach (ObjectTemplateItem item in _items)
            {
                if (item.CategoryFormat == categoryFormat)
                    result.Add(item);
            }
            return result;
        }
    }

    public ObjectTemplateItem? TryLoadFromFile(string filePath)
    {
        lock (_lock)
        {
            ObjectTemplateItem? item = FindByFilePathLocked(filePath);
            if (item != null) return item;
        }

        return LoadFromFile(filePath);
    }

    private ObjectTemplateItem? LoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            string name = Path.GetFileNameWithoutExtension(filePath);
            using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode == null) return null;

            return ObjectTemplateItem.FromJson(jsonNode, name, filePath, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load template from file: {FilePath}", filePath);
            return null;
        }
    }

    private void SaveItemToFile(ObjectTemplateItem item)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            string filePath = Path.Combine(_directoryPath, item.Name.Value + ".json");
            JsonNode json = ObjectTemplateItem.ToJson(item);

            using FileStream stream = File.Create(filePath);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            json.WriteTo(writer);

            item.FilePath = filePath;
            _logger.LogInformation("Saved ObjectTemplateItem to file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save ObjectTemplateItem: {Name}", item.Name.Value);
        }
    }

    private void DeleteItemFile(ObjectTemplateItem item)
    {
        try
        {
            string? filePath = item.FilePath;
            if (filePath != null && File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted ObjectTemplateItem file: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ObjectTemplateItem file: {Name}", item.Name.Value);
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

                // 既に読み込まれているファイルパスを集める
                var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectTemplateItem item in _items)
                {
                    if (item.FilePath != null)
                        loadedPaths.Add(item.FilePath);
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
