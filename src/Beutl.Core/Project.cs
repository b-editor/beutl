using System.Diagnostics;
using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl;

public static class ProjectVariableKeys
{
    public const string FrameRate = "framerate";
    public const string SampleRate = "samplerate";
}

public sealed class Project : Hierarchical
{
    public static readonly CoreProperty<HierarchicalList<ProjectItem>> ItemsProperty;
    public static readonly CoreProperty<Dictionary<string, string>> VariablesProperty;
    public static readonly CoreProperty<string> AppVersionProperty;
    public static readonly CoreProperty<string> MinAppVersionProperty;
    private readonly HierarchicalList<ProjectItem> _items;

    public const string DefaultMinAppVersion = "2.0.0-preview.1";

    static Project()
    {
        ItemsProperty = ConfigureProperty<HierarchicalList<ProjectItem>, Project>(nameof(Items))
            .Accessor(o => o.Items, (o, v) => o.Items = v)
            .Register();

        VariablesProperty = ConfigureProperty<Dictionary<string, string>, Project>(nameof(Variables))
            .Accessor(o => o.Variables)
            .Register();

        AppVersionProperty = ConfigureProperty<string, Project>(nameof(AppVersion))
            .Accessor(o => o.AppVersion)
            .Register();

        MinAppVersionProperty = ConfigureProperty<string, Project>(nameof(MinAppVersion))
            .Accessor(o => o.MinAppVersion)
            .DefaultValue(DefaultMinAppVersion)
            .Register();
    }

    public Project()
    {
        MinAppVersion = DefaultMinAppVersion;
        _items = new HierarchicalList<ProjectItem>(this);
    }

    public string AppVersion { get; private set; } = BeutlApplication.Version;

    public string MinAppVersion { get; private set; }

    [NotAutoSerialized]
    public HierarchicalList<ProjectItem> Items
    {
        get => _items;
        set => _items.Replace(value);
    }

    [NotAutoSerialized]
    public Dictionary<string, string> Variables { get; } = [];

    /// <summary>
    /// Adds <paramref name="item"/> to <see cref="Items"/> and runs <paramref name="persist"/>.
    /// If <paramref name="persist"/> throws, a newly added item is removed again so the in-memory
    /// project stays consistent with what was actually persisted. Items already present are left
    /// untouched (and are not added a second time).
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="persist">Persists the project (e.g. writes it to disk). Rethrown on failure.</param>
    public void AddAndPersist(ProjectItem item, Action persist)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persist);

        if (_items.Contains(item))
        {
            // Already a member; nothing to roll back if persisting fails.
            persist();
            return;
        }

        _items.Add(item);
        ProjectPersistence.PersistOrRollback(persist, () => _items.Remove(item));
    }

    /// <summary>
    /// Removes <paramref name="item"/> from <see cref="Items"/> and runs <paramref name="persist"/>.
    /// If <paramref name="persist"/> throws, the item is re-inserted at its original index so the
    /// in-memory project stays consistent with what was actually persisted. Items not present are
    /// left untouched.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <param name="persist">Persists the project (e.g. writes it to disk). Rethrown on failure.</param>
    public void RemoveAndPersist(ProjectItem item, Action persist)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persist);

        int index = _items.IndexOf(item);
        if (index < 0)
        {
            // Not a member; nothing to roll back if persisting fails.
            persist();
            return;
        }

        _items.RemoveAt(index);
        ProjectPersistence.PersistOrRollback(persist, () => _items.Insert(index, item));
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Deserialize");
        base.Deserialize(context);

        if (context.GetValue<ProjectItem[]>("items") is { } items)
        {
            Items.Replace(items);
        }

        if (context.GetValue<Dictionary<string, string>>("variables") is { } vars)
        {
            Variables.Clear();
            foreach (KeyValuePair<string, string> item in vars)
            {
                Variables.Add(item.Key, item.Value);
            }
        }

        activity?.SetTag("appVersion", BeutlApplication.Version);
        activity?.SetTag("minAppVersion", DefaultMinAppVersion);
        activity?.SetTag("itemsCount", Items.Count);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Serialize");
        activity?.SetTag("appVersion", BeutlApplication.Version);
        activity?.SetTag("minAppVersion", DefaultMinAppVersion);
        activity?.SetTag("itemsCount", Items.Count);

        base.Serialize(context);

        context.SetValue("appVersion", BeutlApplication.Version);
        context.SetValue("minAppVersion", DefaultMinAppVersion);

        context.SetValue("items", Items);

        context.SetValue("variables", Variables);
    }
}
