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

    public override void Deserialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Deserialize");
        base.Deserialize(context);

        if (context.GetValue<string>("appVersion") is { } appVersion)
        {
            AppVersion = appVersion;
        }

        if (context.GetValue<string>("minAppVersion") is { } minAppVersion)
        {
            MinAppVersion = minAppVersion;
        }

        if (context.GetValue<ProjectItem[]>("items") is { } items)
        {
            Items.Replace(items);
            if (items.Any(static item => item.HasMigratedPersistedContent))
            {
                MarkAsMigrated();
            }
        }

        if (context.GetValue<Dictionary<string, string>>("variables") is { } vars)
        {
            Variables.Clear();
            foreach (KeyValuePair<string, string> item in vars)
            {
                Variables.Add(item.Key, item.Value);
            }
        }

        activity?.SetTag("appVersion", AppVersion);
        activity?.SetTag("minAppVersion", MinAppVersion);
        activity?.SetTag("itemsCount", Items.Count);
    }

    // Call only after a migration has rewritten persisted content. Project-item migrations are
    // aggregated during deserialization; a plain load/save keeps the version from disk.
    internal void MarkAsMigrated()
    {
        AppVersion = BeutlApplication.Version;
        MinAppVersion = DefaultMinAppVersion;
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

        context.SetValue("items", Items);

        context.SetValue("variables", Variables);
    }
}
