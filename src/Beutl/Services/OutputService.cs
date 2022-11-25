using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Api.Services;
using Beutl.Framework;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Services;

public sealed class OutputQueueItem : IDisposable
{
    public OutputQueueItem(IOutputContext context)
    {
        Context = context;
        Name = Path.GetFileName(context.TargetFile);

        Context.Started += OnStarted;
        Context.Finished += OnFinished;
    }

    public IOutputContext Context { get; }

    public string Name { get; }

    private void OnStarted(object? sender, EventArgs e)
    {
        EditorService editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        if (editorService.TryGetTabItem(Context.TargetFile, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = false;
        }
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        EditorService editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        if (editorService.TryGetTabItem(Context.TargetFile, out EditorTabItem? tabItem))
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

    public static JsonNode ToJson(OutputQueueItem item)
    {
        JsonNode ctxJson = new JsonObject();
        item.Context.WriteToJson(ref ctxJson);
        return new JsonObject
        {
            ["Extension"] = TypeFormat.ToString(item.Context.Extension.GetType()),
            ["File"] = item.Context.TargetFile,
            [nameof(Context)] = ctxJson
        };
    }

    public static OutputQueueItem? FromJson(JsonNode json)
    {
        try
        {
            var obj = json.AsObject();
            var contextJson = json[nameof(Context)];

            var extensionStr = obj["Extension"]!.AsValue().GetValue<string>();
            var extensionType = TypeFormat.ToType(extensionStr);
            ExtensionProvider provider = ServiceLocator.Current.GetRequiredService<ExtensionProvider>();
            OutputExtension? extension = Array.Find(provider.GetExtensions<OutputExtension>(), x => x.GetType() == extensionType);

            string file = obj["File"]!.AsValue().GetValue<string>();

            if (contextJson != null
                && extension != null
                && File.Exists(file)
                && extension.TryCreateContext(file, out IOutputContext? context))
            {
                context.ReadFromJson(contextJson);
                return new OutputQueueItem(context);
            }
            else
            {
                return null;
            }
        }
        catch
        {
            return null;
        }
    }
}

public sealed class OutputService
{
    private readonly CoreList<OutputQueueItem> _items;
    private readonly ReactivePropertySlim<OutputQueueItem?> _selectedItem = new();
    private readonly string _filePath;

    public OutputService()
    {
        _items = new CoreList<OutputQueueItem>();

        _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "outputlist.json");
    }

    public ICoreList<OutputQueueItem> Items => _items;

    public IReactiveProperty<OutputQueueItem?> SelectedItem => _selectedItem;

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

        var item = new OutputQueueItem(context);
        Items.Add(item);
        SelectedItem.Value = item;
    }

    public OutputExtension[] GetExtensions(string file)
    {
        return ServiceLocator.Current.GetRequiredService<ExtensionProvider>()
            .GetExtensions<OutputExtension>()
            .Where(x => x.IsSupported(file)).ToArray();
    }

    public void SaveItems()
    {
        var array = new JsonArray();
        foreach (OutputQueueItem item in _items.GetMarshal().Value)
        {
            JsonNode json = OutputQueueItem.ToJson(item);
            array.Add(json);
        }

        using FileStream stream = File.Create(_filePath);
        using var writer = new Utf8JsonWriter(stream);
        array.WriteTo(writer);
    }

    public void RestoreItems()
    {
        if (File.Exists(_filePath))
        {
            using FileStream stream = File.Open(_filePath, FileMode.Open);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode is JsonArray jsonArray)
            {
                _items.Clear();
                _items.EnsureCapacity(jsonArray.Count);

                foreach (JsonNode? jsonItem in jsonArray)
                {
                    if (jsonItem != null)
                    {
                        var item = OutputQueueItem.FromJson(jsonItem);
                        if (item != null)
                        {
                            _items.Add(item);
                        }
                    }
                }
            }
        }
    }
}
