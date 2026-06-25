using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class OutputProfileItem : IDisposable
{
    private readonly ILogger<OutputProfileItem> _logger = Log.CreateLogger<OutputProfileItem>();
    private readonly EditorService _editorService;

    public OutputProfileItem(IOutputContext context, IEditorContext editorContext, EditorService editorService)
    {
        Context = context;
        EditorContext = editorContext;
        _editorService = editorService;

        Context.Started += OnStarted;
        Context.Finished += OnFinished;
        _logger.LogInformation("OutputProfileItem created. File: {File}, Context: {Context}", Context.Object.Uri,
            Context);
    }

    public IOutputContext Context { get; }

    public IEditorContext EditorContext { get; }

    private void OnStarted(object? sender, EventArgs e)
    {
        _logger.LogDebug("Output started for file: {File}", Context.Object.Uri);

        if (_editorService.TryGetTabItem(Context.Object, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = false;
            _logger.LogDebug("Tab item disabled for file: {File}", Context.Object.Uri);
        }
        else
        {
            _logger.LogWarning("Tab item not found for file: {File}", Context.Object.Uri);
        }
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        _logger.LogDebug("Output finished for file: {File}", Context.Object.Uri);

        if (_editorService.TryGetTabItem(Context.Object, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = true;
            _logger.LogDebug("Tab item enabled for file: {File}", Context.Object.Uri);
        }
        else
        {
            _logger.LogWarning("Tab item not found for file: {File}", Context.Object.Uri);
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing OutputProfileItem for file: {File}", Context.Object.Uri);
        Context.Started -= OnStarted;
        Context.Finished -= OnFinished;
        Context.Dispose();
    }

    public static JsonNode ToJson(OutputProfileItem item)
    {
        var ctxJson = new JsonObject();
        item.Context.WriteToJson(ctxJson);
        return new JsonObject
        {
            ["Extension"] = TypeFormat.ToString(item.Context.Extension.GetType()),
            ["File"] = item.Context.Object.Uri!.LocalPath,
            [nameof(Context)] = ctxJson
        };
    }

    public static OutputProfileItem? FromJson(IEditorContext editorContext, JsonNode json, ILogger logger,
        ExtensionProvider extensionProvider, EditorService editorService)
    {
        try
        {
            JsonObject obj = json.AsObject();
            JsonNode? contextJson = json[nameof(Context)];

            string extensionStr = obj["Extension"]!.AsValue().GetValue<string>();
            Type? extensionType = TypeFormat.ToType(extensionStr);
            OutputExtension? extension = Array.Find(extensionProvider.GetExtensions<OutputExtension>(),
                x => x.GetType() == extensionType);

            string file = obj["File"]!.AsValue().GetValue<string>();

            if (contextJson != null
                && extension != null
                && File.Exists(file)
                && extension.TryCreateContext(editorContext, out IOutputContext? context))
            {
                context.ReadFromJson(contextJson.AsObject());
                logger.LogInformation("OutputProfileItem created from JSON. File: {File}, Context: {Context}", file,
                    context);
                return new OutputProfileItem(context, editorContext, editorService);
            }
            else
            {
                logger.LogWarning("Failed to create OutputProfileItem from JSON. File: {File}, Extension: {Extension}",
                    file, extensionStr);
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating OutputProfileItem from JSON.");
            return null;
        }
    }
}

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
        if (!extension.TryCreateContext(editViewModel, out IOutputContext? context))
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

    public void SaveItems()
    {
        if (!_isRestored) return;

        var array = new JsonArray();
        foreach (OutputProfileItem item in _items.GetMarshal().Value)
        {
            JsonNode json = OutputProfileItem.ToJson(item);
            array.Add(json);
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

            using FileStream stream = File.Open(_filePath, FileMode.Open);
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

            _items.EnsureCapacity(jsonArray.Count);

            foreach (JsonNode? jsonItem in jsonArray)
            {
                if (jsonItem == null) continue;

                var item = OutputProfileItem.FromJson(editViewModel, jsonItem, _logger, _extensionProvider, _editorService);
                if (item != null)
                {
                    _items.Add(item);
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
