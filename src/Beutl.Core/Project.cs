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
            .DefaultValue(FileFormat.Floor)
            .Register();
    }

    public Project()
    {
        _items = new HierarchicalList<ProjectItem>(this);
    }

    /// <summary>
    /// Gets the version of Beutl that last saved the project file, as the file records it, or the running
    /// version for a project that has no file yet.
    /// </summary>
    public string AppVersion => RootFileVersions.GetAppVersion(this) ?? BeutlApplication.Version;

    /// <summary>
    /// Gets the oldest version of Beutl that can open the project: what its file records, raised to what the
    /// scene and element files read or written with it record, or <see cref="FileFormat.Floor"/> for a
    /// project whose files record none or that has no file yet.
    /// </summary>
    /// <remarks>
    /// A save of the project file writes the higher of this and <see cref="FileFormat.Floor"/>.
    /// </remarks>
    public string MinAppVersion => RootFileVersions.GetMinAppVersion(this) ?? FileFormat.Floor;

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

        activity?.SetTag("appVersion", AppVersion);
        activity?.SetTag("minAppVersion", MinAppVersion);
        activity?.SetTag("itemsCount", Items.Count);
    }

    // The project file's appVersion and minAppVersion are not members: the save that writes the file
    // writes them (see FileFormat).
    public override void Serialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Serialize");
        activity?.SetTag("itemsCount", Items.Count);

        base.Serialize(context);

        context.SetValue("items", Items);

        context.SetValue("variables", Variables);
    }
}
