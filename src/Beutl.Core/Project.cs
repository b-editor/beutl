using System.Diagnostics;
using System.Globalization;
using Beutl.Collections;
using Beutl.JsonConverters;
using Beutl.Serialization;

namespace Beutl;

public static class ProjectVariableKeys
{
    public const string FrameRate = "framerate";
    public const string SampleRate = "samplerate";
}

public sealed class Project : Hierarchical
{
    private readonly HierarchicalList<ProjectItem> _items;

    public Project()
    {
        _items = new HierarchicalList<ProjectItem>(this);
        // The list and the dictionary are values rather than defaults, as their CoreProperty forms had no default.
        ItemsProperty.Value = _items;
        VariablesProperty.Value = Variables;
        ScanProperties<Project>();
    }

    // Serialize and Deserialize write and read the items and variables themselves.
    [NotAutoSerialized]
    public IProperty<HierarchicalList<ProjectItem>> ItemsProperty { get; } = new ValueOnlyProperty<HierarchicalList<ProjectItem>>(null!) { KeepsList = true };

    [NotAutoSerialized]
    public IProperty<Dictionary<string, string>> VariablesProperty { get; } = new ValueOnlyProperty<Dictionary<string, string>>(null!);

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

    public HierarchicalList<ProjectItem> Items
    {
        get => _items;
        set => _items.Replace(value);
    }

    public Dictionary<string, string> Variables { get; } = [];

    // The rate GetFrameRate gives, read here so that the project's own read can use it.
    internal static Rational ReadFrameRate(Project? project)
        => project?.Variables.TryGetValue(ProjectVariableKeys.FrameRate, out string? value) == true
           && Rational.TryParse(value, CultureInfo.InvariantCulture, out Rational rate)
           && rate.Numerator > 0 && rate.Denominator > 0
            ? rate
            : new Rational(30);

    public override void Deserialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Deserialize");
        base.Deserialize(context);

        // The variables come first: the items are read on the project's frame grid.
        if (context.GetValue<Dictionary<string, string>>("variables") is { } vars)
        {
            Variables.Clear();
            foreach (KeyValuePair<string, string> item in vars)
            {
                Variables.Add(item.Key, item.Value);
            }
        }

        using (MediaTimeJsonConverter.ReadOnFrameGrid(ReadFrameRate(this)))
        {
            if (context.GetValue<ProjectItem[]>("items") is { } items)
            {
                Items.Replace(items);
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
