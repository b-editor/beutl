using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public class OutputPresetItem(OutputExtension extension, JsonObject json, string name)
{
    public OutputExtension Extension { get; } = extension;

    public JsonObject Json { get; } = json;

    public ReactiveProperty<string> Name { get; } = new(name);

    public void Apply(ISupportOutputPreset context)
    {
        if (((IOutputContext)context).Extension.GetType() != Extension.GetType())
        {
            throw new InvalidOperationException("Extension type mismatch.");
        }

        context.Apply(Json);
    }

    public static OutputPresetItem CreateFromContext(ISupportOutputPreset context, string name)
    {
        return new OutputPresetItem(
            ((IOutputContext)context).Extension,
            context.ToPreset(),
            name);
    }

    public static JsonNode ToJson(OutputPresetItem item)
    {
        return new JsonObject
        {
            [nameof(Extension)] = TypeFormat.ToString(item.Extension.GetType()),
            [nameof(Json)] = item.Json.DeepClone(),
            [nameof(Name)] = item.Name.Value
        };
    }

    public static OutputPresetItem? FromJson(JsonNode json, ILogger logger)
    {
        try
        {
            string? typeName = json[nameof(Extension)]?.ToString();
            if (typeName == null)
            {
                logger.LogError("Extension type name is null.");
                return null;
            }

            Type? type = TypeFormat.ToType(typeName);
            if (type == null)
            {
                logger.LogError("Extension type not found: {TypeName}", typeName);
                return null;
            }

            if (Activator.CreateInstance(type) is not OutputExtension extension)
            {
                logger.LogError("Failed to create instance of extension type: {TypeName}", typeName);
                return null;
            }

            if (json[nameof(Json)] is not JsonObject jsonObject)
            {
                logger.LogError("Json object is null.");
                return null;
            }

            if (json[nameof(Name)] is not JsonValue jsonValue || !jsonValue.TryGetValue(out string? name))
            {
                logger.LogError("Name is null.");
                return null;
            }

            return new OutputPresetItem(extension, jsonObject, name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating OutputPresetItem from JSON.");
            return null;
        }
    }
}

public sealed class OutputPresetService
{
    public static readonly OutputPresetService Instance = new();
    private readonly CoreList<OutputPresetItem> _items = [];

    private readonly string _filePath = Path.Combine(
        BeutlEnvironment.GetHomeDirectoryPath(), "output-presets.json");

    private readonly ILogger _logger = Log.CreateLogger<OutputPresetService>();
    private bool _isRestored;

    private OutputPresetService()
    {
        RestoreItems();
    }

    public ICoreList<OutputPresetItem> Items => _items;

    public void AddItem(IOutputContext context, string name)
    {
        if (context is not ISupportOutputPreset outputPreset) return;
        var item = OutputPresetItem.CreateFromContext(outputPreset, name);
        Items.Add(item);
        _logger.LogInformation("Added new OutputPresetItem. Context: {Context}", context);
    }

    public void SaveItems()
    {
        if (!_isRestored) return;

        var array = new JsonArray();
        foreach (OutputPresetItem item in _items)
        {
            JsonNode json = OutputPresetItem.ToJson(item);
            array.Add(json);
        }

        using FileStream stream = File.Create(_filePath);
        using var writer = new Utf8JsonWriter(stream);
        array.WriteTo(writer);
        _logger.LogInformation("Saved {Count} OutputPresetItem to file: {FilePath}", _items.Count, _filePath);
    }

    public void RestoreItems()
    {
        try
        {
            _isRestored = true;
            if (!File.Exists(_filePath))
            {
                _logger.LogWarning("Output preset file not found: {FilePath}", _filePath);
                return;
            }

            using FileStream stream = File.Open(_filePath, FileMode.Open);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode is not JsonArray jsonArray)
            {
                _logger.LogWarning("Invalid JSON format in output preset file: {FilePath}", _filePath);
                return;
            }

            _items.Clear();

            _items.EnsureCapacity(jsonArray.Count);

            foreach (JsonNode? jsonItem in jsonArray)
            {
                if (jsonItem == null) continue;

                var item = OutputPresetItem.FromJson(jsonItem, _logger);
                if (item != null)
                {
                    _items.Add(item);
                }
            }

            _logger.LogInformation("Restored {Count} OutputPresetItem from file: {FilePath}", _items.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring output preset file.");
        }
    }
}
