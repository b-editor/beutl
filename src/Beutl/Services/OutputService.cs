using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class OutputService(EditViewModel editViewModel) : IDisposable
{
    private readonly CoreList<OutputProfileItem> _items = [];
    private readonly ReactivePropertySlim<OutputProfileItem?> _selectedItem = new();
    private readonly EditorService _editorService = editViewModel.EditorService;
    private readonly ExtensionProvider _extensionProvider = editViewModel.ExtensionProvider;

    private readonly string _filePath = Path.Combine(
        Path.GetDirectoryName(editViewModel.Scene.Uri!.LocalPath)!,
        EditorConstants.BeutlFolder, "output-profile.json");

    private readonly ILogger _logger = Log.CreateLogger<OutputService>();
    private bool _isRestored;

    public ICoreList<OutputProfileItem> Items => _items;

    public IReactiveProperty<OutputProfileItem?> SelectedItem => _selectedItem;

    public void AddItem(string file, OutputExtension extension)
    {
        if (!extension.TryCreateContext(
                editViewModel,
                out IOutputContext? context))
        {
            _logger.LogError("Failed to create context for file: {File}", file);
            throw new Exception("Failed to create context");
        }

        context.Name.Value = Items.Count == 0 ? "Default" : $"Profile {Items.Count}";
        var item = new OutputProfileItem(context, editViewModel, _editorService);
        Items.Add(item);
        SelectedItem.Value = item;
        _logger.LogInformation("Added new OutputProfileItem. File: {File}, Context: {Context}", file, context);
    }

    public OutputExtension[] GetExtensions(Type type)
    {
        return _extensionProvider
            .GetExtensions<OutputExtension>()
            .Where(x => x.IsSupported(type)).ToArray();
    }

    private readonly List<(OutputProfileItem? Item, JsonNode? Unavailable)> _restoredProfileOrder = [];

    public void SaveItems()
    {
        if (!_isRestored) return;

        var array = new JsonArray();
        var current = _items.ToHashSet();
        var before = new Dictionary<OutputProfileItem, List<JsonNode>>();
        var pending = new List<JsonNode>();
        foreach (var slot in _restoredProfileOrder)
        {
            if (slot.Unavailable is { } unavailable)
                pending.Add(unavailable);
            else if (slot.Item is { } item && current.Contains(item))
            {
                before[item] = pending;
                pending = [];
            }
        }
        // Hidden profiles stay attached to the next surviving restored profile.
        // New profiles append after the old trailing hidden entries.
        OutputProfileItem? lastRestored = _items.LastOrDefault(before.ContainsKey);
        if (lastRestored is null)
            foreach (JsonNode unavailable in pending) array.Add(unavailable.DeepClone());
        foreach (OutputProfileItem item in _items.GetMarshal().Value)
        {
            if (before.TryGetValue(item, out List<JsonNode>? hidden))
                foreach (JsonNode unavailable in hidden) array.Add(unavailable.DeepClone());
            array.Add(OutputProfileItem.ToJson(item));
            if (ReferenceEquals(item, lastRestored))
                foreach (JsonNode unavailable in pending) array.Add(unavailable.DeepClone());
        }
        array.JsonSave(_filePath);
        _logger.LogInformation("Saved {Count} OutputProfileItems to file: {FilePath}", _items.Count, _filePath);
    }

    public void RestoreItems()
    {
        // 再試行時に前回の成功状態が残ると、IO/JSON 失敗後も SaveItems が有効になり
        // 空または不整合な _items が書き込まれてユーザーのプロファイルを消す恐れがある。
        _isRestored = false;
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogWarning("Output profile file not found: {FilePath}", _filePath);
                _isRestored = true;
                return;
            }

            using FileStream stream = File.OpenRead(_filePath);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode is not JsonArray jsonArray)
            {
                _logger.LogWarning("Invalid JSON format in output profile file: {FilePath}", _filePath);
                return;
            }

            var items = _items.ToArray();
            _items.Clear();
            _selectedItem.Value = null;
            foreach (OutputProfileItem item in items)
            {
                item.Dispose();
            }

            _restoredProfileOrder.Clear();
            _items.EnsureCapacity(jsonArray.Count);

            foreach (JsonNode? jsonItem in jsonArray)
            {
                if (jsonItem == null) continue;

                var item = OutputProfileItem.FromJson(editViewModel, jsonItem, _logger, _extensionProvider, _editorService);
                if (item != null)
                {
                    _items.Add(item);
                    _restoredProfileOrder.Add((item, null));
                }
                else
                {
                    _restoredProfileOrder.Add((null, jsonItem.DeepClone()));
                }
            }

            // 既存ファイルの読み込みに成功した場合のみ書き込みを許可する。
            // try 冒頭で true にすると、IO 失敗や JSON 破損時に後続の SaveItems が
            // 空の _items を書き込み、ユーザーの保存済みプロファイルが消える。
            _isRestored = true;
            _logger.LogInformation("Restored {Count} OutputProfileItems from file: {FilePath}", _items.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring output profile file: {FilePath}", _filePath);
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing OutputService.");

        var items = _items.ToArray();
        _items.Clear();
        _selectedItem.Value = null;
        _selectedItem.Dispose();
        foreach (OutputProfileItem item in items)
        {
            item.Dispose();
        }

        _logger.LogInformation("OutputService disposed.");
    }
}
