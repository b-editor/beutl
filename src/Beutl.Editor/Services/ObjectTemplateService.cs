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

    private readonly ILogger _logger = Log.CreateLogger<ObjectTemplateService>();

    private ObjectTemplateService()
    {
        RestoreItems();
    }

    public string DirectoryPath => _directoryPath;

    public ICoreList<ObjectTemplateItem> Items => _items;

    public ObjectTemplateItem AddFromInstance(ICoreSerializable instance, string name)
    {
        var item = ObjectTemplateItem.CreateFromInstance(instance, name);
        SaveItemToFile(item);
        _items.Add(item);
        _logger.LogInformation("Added new ObjectTemplateItem: {Name}", name);
        return item;
    }

    public void Remove(ObjectTemplateItem item)
    {
        _items.Remove(item);
        DeleteItemFile(item);
        _logger.LogInformation("Removed ObjectTemplateItem: {Name}", item.Name.Value);
    }

    public void Rename(ObjectTemplateItem item, string newName)
    {
        string oldName = item.Name.Value;
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
        _logger.LogInformation("Renamed ObjectTemplateItem from {OldName} to {NewName}", oldName, newName);
    }

    public ObjectTemplateItem? FindById(Guid id)
    {
        foreach (ObjectTemplateItem item in _items)
        {
            if (item.Id == id) return item;
        }

        return null;
    }

    public IEnumerable<ObjectTemplateItem> FindByBaseType(Type baseType)
    {
        foreach (ObjectTemplateItem item in _items)
        {
            if (item.BaseType == baseType)
                yield return item;
        }
    }

    public IEnumerable<ObjectTemplateItem> FindByCategory(string categoryFormat)
    {
        foreach (ObjectTemplateItem item in _items)
        {
            if (item.CategoryFormat == categoryFormat)
                yield return item;
        }
    }

    /// <summary>
    /// ファイルパスからテンプレートを取得する。登録済みのアイテムから検索し、
    /// 見つからなければファイルを直接読み込む。
    /// </summary>
    public ObjectTemplateItem? TryLoadFromFile(string filePath)
    {
        foreach (ObjectTemplateItem item in _items)
        {
            if (string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        try
        {
            if (!File.Exists(filePath)) return null;

            string name = Path.GetFileNameWithoutExtension(filePath);
            using FileStream stream = File.Open(filePath, FileMode.Open);
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

            _items.Clear();

            foreach (string filePath in Directory.EnumerateFiles(
                         _directoryPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(filePath);
                    using FileStream stream = File.Open(filePath, FileMode.Open);
                    var jsonNode = JsonNode.Parse(stream);
                    if (jsonNode == null) continue;

                    var item = ObjectTemplateItem.FromJson(jsonNode, name, filePath, _logger);
                    if (item != null)
                    {
                        _items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore template from file: {FilePath}", filePath);
                }
            }

            _logger.LogInformation("Restored {Count} ObjectTemplateItem from directory: {DirectoryPath}",
                _items.Count, _directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring templates.");
        }
    }
}
