using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class OutputProfileItem : IDisposable
{
    private readonly ILogger<OutputProfileItem> _logger = Log.CreateLogger<OutputProfileItem>();

    public OutputProfileItem(IOutputContext context, IEditorContext editorContext)
    {
        Context = context;
        EditorContext = editorContext;

        Context.Started += OnStarted;
        Context.Finished += OnFinished;
        _logger.LogInformation("OutputProfileItem created. File: {File}, Context: {Context}", Context.TargetFile,
            Context);
    }

    public IOutputContext Context { get; }

    public IEditorContext EditorContext { get; }

    private void OnStarted(object? sender, EventArgs e)
    {
        _logger.LogDebug("Output started for file: {File}", Context.TargetFile);

        if (EditorService.Current.TryGetTabItem(Context.TargetFile, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = false;
            _logger.LogDebug("Tab item disabled for file: {File}", Context.TargetFile);
        }
        else
        {
            _logger.LogWarning("Tab item not found for file: {File}", Context.TargetFile);
        }
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        _logger.LogDebug("Output finished for file: {File}", Context.TargetFile);

        if (EditorService.Current.TryGetTabItem(Context.TargetFile, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = true;
            _logger.LogDebug("Tab item enabled for file: {File}", Context.TargetFile);
        }
        else
        {
            _logger.LogWarning("Tab item not found for file: {File}", Context.TargetFile);
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing OutputProfileItem for file: {File}", Context.TargetFile);
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
            ["File"] = item.Context.TargetFile,
            [nameof(Context)] = ctxJson
        };
    }

    public static OutputProfileItem? FromJson(IEditorContext editorContext, JsonNode json, ILogger logger)
    {
        try
        {
            JsonObject obj = json.AsObject();
            JsonNode? contextJson = json[nameof(Context)];

            string extensionStr = obj["Extension"]!.AsValue().GetValue<string>();
            Type? extensionType = TypeFormat.ToType(extensionStr);
            ExtensionProvider provider = ExtensionProvider.Current;
            OutputExtension? extension = Array.Find(provider.GetExtensions<OutputExtension>(),
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
                return new OutputProfileItem(context, editorContext);
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

    private readonly string _filePath = Path.Combine(
        Path.GetDirectoryName(editViewModel.Scene.FileName)!, Constants.BeutlFolder, "output-profile.json");

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
        var item = new OutputProfileItem(context, editViewModel);
        Items.Add(item);
        SelectedItem.Value = item;
        _logger.LogInformation("Added new OutputProfileItem. File: {File}, Context: {Context}", file, context);
    }

    public static OutputExtension[] GetExtensions(string file)
    {
        return ExtensionProvider.Current
            .GetExtensions<OutputExtension>()
            .Where(x => x.IsSupported(file)).ToArray();
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

        using FileStream stream = File.Create(_filePath);
        using var writer = new Utf8JsonWriter(stream);
        array.WriteTo(writer);
        _logger.LogInformation("Saved {Count} OutputProfileItems to file: {FilePath}", _items.Count, _filePath);
    }

    public void RestoreItems()
    {
        _isRestored = true;
        if (!File.Exists(_filePath))
        {
            _logger.LogWarning("Output profile file not found: {FilePath}", _filePath);
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
        _selectedItem.Dispose();
        foreach (OutputProfileItem item in items)
        {
            item.Dispose();
        }

        _items.EnsureCapacity(jsonArray.Count);

        foreach (JsonNode? jsonItem in jsonArray)
        {
            if (jsonItem == null) continue;

            var item = OutputProfileItem.FromJson(editViewModel, jsonItem, _logger);
            if (item != null)
            {
                _items.Add(item);
            }
        }

        _logger.LogInformation("Restored {Count} OutputProfileItems from file: {FilePath}", _items.Count, _filePath);
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
