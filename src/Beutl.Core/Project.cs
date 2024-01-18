using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl;

public static class ProjectVariableKeys
{
    public const string FrameRate = "framerate";
    public const string SampleRate = "samplerate";
}

public sealed class Project : Hierarchical, IStorable
{
    public static readonly CoreProperty<string> AppVersionProperty;
    public static readonly CoreProperty<string> MinAppVersionProperty;
    private string? _rootDirectory;
    private string? _fileName;
    private EventHandler? _saved;
    private EventHandler? _restored;
    private readonly HierarchicalList<ProjectItem> _items;
    private readonly Dictionary<string, string> _variables = [];

    static Project()
    {
        AppVersionProperty = ConfigureProperty<string, Project>(nameof(AppVersion))
            .Accessor(o => o.AppVersion)
            .Register();

        MinAppVersionProperty = ConfigureProperty<string, Project>(nameof(MinAppVersion))
            .Accessor(o => o.MinAppVersion)
            .DefaultValue("1.0.0-preview1")
            .Register();
    }

    public Project()
    {
        MinAppVersion = "1.0.0-preview1";
        _items = new HierarchicalList<ProjectItem>(this);
        _items.CollectionChanged += Items_CollectionChanged;
    }

    event EventHandler IStorable.Saved
    {
        add => _saved += value;
        remove => _saved -= value;
    }

    event EventHandler IStorable.Restored
    {
        add => _restored += value;
        remove => _restored -= value;
    }

    public string RootDirectory => _rootDirectory ?? throw new Exception("The file name is not set.");

    public string FileName => _fileName ?? throw new Exception("The file name is not set.");

    public string AppVersion { get; private set; } = GitVersionInformation.NuGetVersionV2;

    public string MinAppVersion { get; private set; }

    public DateTime LastSavedTime { get; private set; }

    public ICoreList<ProjectItem> Items => _items;

    public IDictionary<string, string> Variables => _variables;

    public void Restore(string filename)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Restore");
        activity?.SetTag("filenameHash", filename.GetMD5Hash());

        _fileName = filename;
        _rootDirectory = Path.GetDirectoryName(filename);

        this.JsonRestore2(filename);
        LastSavedTime = File.GetLastWriteTimeUtc(filename);

        _restored?.Invoke(this, EventArgs.Empty);
    }

    public void Save(string filename)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Save");
        activity?.SetTag("filenameHash", filename.GetMD5Hash());

        _fileName = filename;
        _rootDirectory = Path.GetDirectoryName(filename);
        LastSavedTime = DateTime.UtcNow;

        this.JsonSave2(filename);
        File.SetLastWriteTimeUtc(filename, LastSavedTime);

        _saved?.Invoke(this, EventArgs.Empty);
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);

        if (json.TryGetPropertyValue("appVersion", out JsonNode? versionNode)
            && versionNode!.AsValue().TryGetValue(out string? version))
        {
            AppVersion = version;
        }

        if (json.TryGetPropertyValue("minAppVersion", out JsonNode? minVersionNode)
            && minVersionNode!.AsValue().TryGetValue(out string? minVersion))
        {
            MinAppVersion = minVersion;
        }

        if (json.TryGetPropertyValue("items", out JsonNode? itemsNode))
        {
            SyncronizeScenes(itemsNode!.AsArray()
                .Select(i => (string)i!));
        }

        if (json.TryGetPropertyValue("variables", out JsonNode? variablesNode)
            && variablesNode is JsonObject variablesObj)
        {
            Variables.Clear();
            foreach (KeyValuePair<string, JsonNode?> item in variablesObj)
            {
                if (item.Value != null)
                    Variables[item.Key] = item.Value.AsValue().ToString();
            }
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        json["appVersion"] = AppVersion;
        json["minAppVersion"] = MinAppVersion;

        var items = new JsonArray();
        foreach (ProjectItem item in Items)
        {
            string path = Path.GetRelativePath(RootDirectory, item.FileName).Replace('\\', '/');
            var value = JsonValue.Create(path);
            items.Add(value);
        }

        json["items"] = items;

        var variables = new JsonObject();
        foreach (KeyValuePair<string, string> item in Variables)
        {
            variables.Add(item.Key, JsonValue.Create(item.Value));
        }

        json["variables"] = variables;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Serialize");
        base.Deserialize(context);

        AppVersion = context.GetValue<string>("appVersion") ?? AppVersion;
        MinAppVersion = context.GetValue<string>("minAppVersion") ?? MinAppVersion;

        SyncronizeScenes(context.GetValue<string[]>("items")!);

        if (context.GetValue<Dictionary<string, string>>("variables") is { } vars)
        {
            Variables.Clear();
            foreach (KeyValuePair<string, string> item in vars)
            {
                Variables.Add(item);
            }
        }

        activity?.SetTag("appVersion", AppVersion);
        activity?.SetTag("minAppVersion", MinAppVersion);
        activity?.SetTag("itemsCount", Items.Count);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Serialize");
        activity?.SetTag("appVersion", AppVersion);
        activity?.SetTag("minAppVersion", MinAppVersion);
        activity?.SetTag("itemsCount", Items.Count);

        base.Serialize(context);

        context.SetValue("appVersion", AppVersion);
        context.SetValue("minAppVersion", MinAppVersion);

        context.SetValue("items", Items
            .Select(item => Path.GetRelativePath(RootDirectory, item.FileName).Replace('\\', '/')));

        context.SetValue("variables", Variables);
    }

    public void Dispose()
    {
        _items.CollectionChanged -= Items_CollectionChanged;
        _items.Clear();
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_fileName != null)
            Save(_fileName);
    }

    private void SyncronizeScenes(IEnumerable<string> pathToItem)
    {
        _items.CollectionChanged -= Items_CollectionChanged;
        pathToItem = pathToItem.Select(x => Path.GetFullPath(x, RootDirectory)).ToArray();

        // 削除するシーン
        IEnumerable<ProjectItem> toRemoveItems = _items.ExceptBy(pathToItem, x => x.FileName);
        // 追加するシーン
        IEnumerable<string> toAddItems = pathToItem.Except(_items.Select(x => x.FileName));

        foreach (ProjectItem? item in toRemoveItems)
        {
            _items.Remove(item);
        }

        ProjectItemContainer resolver = ProjectItemContainer.Current;
        foreach (string item in toAddItems)
        {
            if (resolver.TryGetOrCreateItem(item, out ProjectItem? projectItem))
            {
                _items.Add(projectItem);
            }
        }

        _items.CollectionChanged += Items_CollectionChanged;
    }
}
