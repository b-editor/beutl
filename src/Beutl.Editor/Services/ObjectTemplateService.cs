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

    private readonly string _filePath = Path.Combine(
        BeutlEnvironment.GetHomeDirectoryPath(), "object-templates.json");

    private readonly ILogger _logger = Log.CreateLogger<ObjectTemplateService>();
    private bool _isRestored;

    private ObjectTemplateService()
    {
        RestoreItems();
    }

    public ICoreList<ObjectTemplateItem> Items => _items;

    public ObjectTemplateItem AddFromInstance(ICoreSerializable instance, string name)
    {
        var item = ObjectTemplateItem.CreateFromInstance(instance, name);
        _items.Add(item);
        SaveItems();
        _logger.LogInformation("Added new ObjectTemplateItem: {Name}", name);
        return item;
    }

    public void Remove(ObjectTemplateItem item)
    {
        _items.Remove(item);
        SaveItems();
        _logger.LogInformation("Removed ObjectTemplateItem: {Name}", item.Name.Value);
    }

    public void Rename(ObjectTemplateItem item, string newName)
    {
        item.Name.Value = newName;
        SaveItems();
        _logger.LogInformation("Renamed ObjectTemplateItem to: {Name}", newName);
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

    public void SaveItems()
    {
        if (!_isRestored) return;

        var array = new JsonArray();
        foreach (ObjectTemplateItem item in _items)
        {
            JsonNode json = ObjectTemplateItem.ToJson(item);
            array.Add(json);
        }

        using FileStream stream = File.Create(_filePath);
        using var writer = new Utf8JsonWriter(stream);
        array.WriteTo(writer);
        _logger.LogInformation("Saved {Count} ObjectTemplateItem to file: {FilePath}", _items.Count, _filePath);
    }

    public void RestoreItems()
    {
        try
        {
            _isRestored = true;
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Object template file not found: {FilePath}", _filePath);
                return;
            }

            using FileStream stream = File.Open(_filePath, FileMode.Open);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode is not JsonArray jsonArray)
            {
                _logger.LogWarning("Invalid JSON format in object template file: {FilePath}", _filePath);
                return;
            }

            _items.Clear();
            _items.EnsureCapacity(jsonArray.Count);

            foreach (JsonNode? jsonItem in jsonArray)
            {
                if (jsonItem == null) continue;

                var item = ObjectTemplateItem.FromJson(jsonItem, _logger);
                if (item != null)
                {
                    _items.Add(item);
                }
            }

            _logger.LogInformation("Restored {Count} ObjectTemplateItem from file: {FilePath}", _items.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring object template file.");
        }
    }
}
