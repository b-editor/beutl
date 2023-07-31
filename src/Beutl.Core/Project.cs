using System.Collections.Specialized;
using System.Reflection;
using System.Text.Json.Nodes;

using Beutl.Collections;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl;

public static class ProjectVariableKeys
{
    public const string FrameRate = "framerate";
    public const string SampleRate = "samplerate";
}

// Todo: IResourceProviderを実装
public sealed class Project : Hierarchical, IStorable
{
    public static readonly CoreProperty<Version> AppVersionProperty;
    public static readonly CoreProperty<Version> MinAppVersionProperty;
    private string? _rootDirectory;
    private string? _fileName;
    private EventHandler? _saved;
    private EventHandler? _restored;
    private readonly HierarchicalList<ProjectItem> _items;
    private readonly Dictionary<string, string> _variables = new();

    static Project()
    {
        AppVersionProperty = ConfigureProperty<Version, Project>(nameof(AppVersion))
            .Accessor(o => o.AppVersion)
            .Register();

        MinAppVersionProperty = ConfigureProperty<Version, Project>(nameof(MinAppVersion))
            .Accessor(o => o.MinAppVersion)
            .DefaultValue(new Version(0, 3))
            .Register();
    }

    public Project()
    {
        MinAppVersion = new Version(0, 3);
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

    public Version AppVersion { get; private set; } = Assembly.GetEntryAssembly()!.GetName().Version ?? new Version();

    public Version MinAppVersion { get; private set; }

    public DateTime LastSavedTime { get; private set; }

    public ICoreList<ProjectItem> Items => _items;

    public IDictionary<string, string> Variables => _variables;

    public void Restore(string filename)
    {
        _fileName = filename;
        _rootDirectory = Path.GetDirectoryName(filename);

        this.JsonRestore(filename);
        LastSavedTime = File.GetLastWriteTimeUtc(filename);

        _restored?.Invoke(this, EventArgs.Empty);
    }

    public void Save(string filename)
    {
        _fileName = filename;
        _rootDirectory = Path.GetDirectoryName(filename);
        LastSavedTime = DateTime.UtcNow;

        this.JsonSave(filename);
        File.SetLastWriteTimeUtc(filename, LastSavedTime);

        _saved?.Invoke(this, EventArgs.Empty);
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);

        if (json.TryGetPropertyValue("appVersion", out JsonNode? versionNode)
            && versionNode!.AsValue().TryGetValue(out Version? version))
        {
            AppVersion = version;
        }

        if (json.TryGetPropertyValue("minAppVersion", out JsonNode? minVersionNode)
            && minVersionNode!.AsValue().TryGetValue(out Version? minVersion))
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

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        json["appVersion"] = JsonValue.Create(AppVersion);
        json["minAppVersion"] = JsonValue.Create(MinAppVersion);

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

        IProjectItemContainer resolver = ServiceLocator.Current.GetRequiredService<IProjectItemContainer>();
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
