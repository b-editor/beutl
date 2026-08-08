using System.Text.Json.Nodes;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

/// <summary>
/// A named dock layout that the user saved from an editor and can re-apply later.
/// </summary>
public sealed class DockLayoutPresetItem(string name, JsonObject layout)
{
    /// <summary>Gets the display name, unique within <see cref="DockLayoutPresetService"/>.</summary>
    public ReactiveProperty<string> Name { get; } = new(name);

    /// <summary>Gets the serialized layout, in <c>DockHostViewModel</c>'s view-state shape.</summary>
    public JsonObject Layout { get; } = layout;

    public static JsonNode ToJson(DockLayoutPresetItem item)
    {
        return new JsonObject
        {
            [nameof(Name)] = item.Name.Value,
            [nameof(Layout)] = item.Layout.DeepClone(),
        };
    }

    public static DockLayoutPresetItem? FromJson(JsonNode json, ILogger logger)
    {
        try
        {
            if (json[nameof(Name)] is not JsonValue nameValue
                || !nameValue.TryGetValue(out string? name)
                || string.IsNullOrWhiteSpace(name))
            {
                logger.LogWarning("Dock layout preset has no name.");
                return null;
            }

            if (json[nameof(Layout)] is not JsonObject layout)
            {
                logger.LogWarning("Dock layout preset '{Name}' has no layout.", name);
                return null;
            }

            return new DockLayoutPresetItem(name, (JsonObject)layout.DeepClone());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating DockLayoutPresetItem from JSON.");
            return null;
        }
    }
}

/// <summary>
/// Stores named dock layouts in <c>$BEUTL_HOME/dock-layout-presets.json</c> so they can be
/// applied to any scene, in any project.
/// </summary>
public sealed class DockLayoutPresetService
{
    public static readonly DockLayoutPresetService Instance = new();

    private readonly CoreList<DockLayoutPresetItem> _items = [];
    private readonly ILogger _logger = Log.CreateLogger<DockLayoutPresetService>();
    private readonly string? _filePathOverride;
    private bool _isRestored;

    private DockLayoutPresetService()
    {
        RestoreItems();
    }

    // Test seam: points the store at a scratch file instead of the ambient BEUTL_HOME.
    internal DockLayoutPresetService(string filePath)
    {
        _filePathOverride = filePath;
        RestoreItems();
    }

    public ICoreList<DockLayoutPresetItem> Items => _items;

    private string FilePath => _filePathOverride
                               ?? Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "dock-layout-presets.json");

    /// <summary>Adds a preset, or overwrites the one already using <paramref name="name"/>.</summary>
    /// <returns>The added or updated item, or null when <paramref name="name"/> is blank.</returns>
    public DockLayoutPresetItem? Save(string name, JsonObject layout)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            _logger.LogWarning("Refused to save a dock layout preset with a blank name.");
            return null;
        }

        var clone = (JsonObject)layout.DeepClone();
        DockLayoutPresetItem? existing = Find(name);
        if (existing is not null)
        {
            int index = _items.IndexOf(existing);
            var replacement = new DockLayoutPresetItem(existing.Name.Value, clone);
            _items[index] = replacement;
            SaveItems();
            _logger.LogInformation("Overwrote dock layout preset '{Name}'.", name);
            return replacement;
        }

        var item = new DockLayoutPresetItem(name, clone);
        _items.Add(item);
        SaveItems();
        _logger.LogInformation("Added dock layout preset '{Name}'.", name);
        return item;
    }

    public bool Remove(DockLayoutPresetItem item)
    {
        if (!_items.Remove(item)) return false;
        SaveItems();
        _logger.LogInformation("Removed dock layout preset '{Name}'.", item.Name.Value);
        return true;
    }

    public bool Rename(DockLayoutPresetItem item, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return false;
        if (!_items.Contains(item)) return false;
        if (string.Equals(item.Name.Value, newName, StringComparison.OrdinalIgnoreCase))
        {
            // Same preset, possibly a case-only change — allow it.
            item.Name.Value = newName;
            SaveItems();
            return true;
        }

        if (Find(newName) is not null) return false;

        item.Name.Value = newName;
        SaveItems();
        return true;
    }

    public DockLayoutPresetItem? Find(string name)
    {
        return _items.FirstOrDefault(
            i => string.Equals(i.Name.Value, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public bool Exists(string name) => Find(name) is not null;

    public void SaveItems()
    {
        // Never overwrite a file we failed to read; a transient IO error would wipe every preset.
        if (!_isRestored) return;

        try
        {
            var array = new JsonArray();
            foreach (DockLayoutPresetItem item in _items)
            {
                array.Add(DockLayoutPresetItem.ToJson(item));
            }

            array.JsonSave(FilePath);
            _logger.LogInformation(
                "Saved {Count} dock layout presets to file: {FilePath}", _items.Count, FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while saving dock layout presets.");
        }
    }

    public void RestoreItems()
    {
        string filePath = FilePath;
        try
        {
            if (!File.Exists(filePath))
            {
                _isRestored = true;
                return;
            }

            using FileStream stream = File.Open(filePath, FileMode.Open);
            JsonNode? jsonNode = JsonNode.Parse(stream);
            if (jsonNode is not JsonArray jsonArray)
            {
                _logger.LogWarning("Invalid JSON format in dock layout preset file: {FilePath}", filePath);
                return;
            }

            _items.Clear();
            _items.EnsureCapacity(jsonArray.Count);
            foreach (JsonNode? jsonItem in jsonArray)
            {
                if (jsonItem is null) continue;
                DockLayoutPresetItem? item = DockLayoutPresetItem.FromJson(jsonItem, _logger);
                if (item is not null && !Exists(item.Name.Value))
                {
                    _items.Add(item);
                }
            }

            _isRestored = true;
            _logger.LogInformation(
                "Restored {Count} dock layout presets from file: {FilePath}", _items.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring dock layout presets.");
        }
    }
}
