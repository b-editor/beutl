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
    public OutputProfileItem(IOutputContext context)
    {
        Context = context;

        Context.Started += OnStarted;
        Context.Finished += OnFinished;
    }

    public IOutputContext Context { get; }

    private void OnStarted(object? sender, EventArgs e)
    {
        if (EditorService.Current.TryGetTabItem(Context.TargetFile, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = false;
        }
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        if (EditorService.Current.TryGetTabItem(Context.TargetFile, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = true;
        }
    }

    public void Dispose()
    {
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

    public static OutputProfileItem? FromJson(JsonNode json, ILogger logger)
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
                && extension.TryCreateContext(file, out IOutputContext? context))
            {
                context.ReadFromJson(contextJson.AsObject());
                return new OutputProfileItem(context);
            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred.");
            return null;
        }
    }
}

public sealed class OutputService(EditViewModel editViewModel)
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
        if (Items.Any(x => x.Context.TargetFile == file))
        {
            throw new Exception("Already added");
        }

        if (!extension.TryCreateContext(file, out IOutputContext? context))
        {
            throw new Exception("Failed to create context");
        }

        context.Name.Value = Items.Count == 0 ? "Default" : $"Profile {Items.Count}";
        var item = new OutputProfileItem(context);
        Items.Add(item);
        SelectedItem.Value = item;
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
    }

    public void RestoreItems()
    {
        _isRestored = true;
        if (!File.Exists(_filePath)) return;

        using FileStream stream = File.Open(_filePath, FileMode.Open);
        var jsonNode = JsonNode.Parse(stream);
        if (jsonNode is not JsonArray jsonArray) return;

        _items.Clear();
        _items.EnsureCapacity(jsonArray.Count);

        foreach (JsonNode? jsonItem in jsonArray)
        {
            if (jsonItem == null) continue;

            var item = OutputProfileItem.FromJson(jsonItem, _logger);
            if (item != null)
            {
                _items.Add(item);
            }
        }
    }
}
