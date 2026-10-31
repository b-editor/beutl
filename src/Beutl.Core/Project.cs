using System.Diagnostics;
using System.Globalization;
using Beutl.JsonConverters;
using Beutl.Serialization;

namespace Beutl;

// The variables that held the rates before 2.0.0-preview.9, which a project reads once into its rate members.
internal static class ProjectVariableKeys
{
    public const string FrameRate = "framerate";
    public const string SampleRate = "samplerate";
}

public sealed partial class Project : Hierarchical
{
    public Project()
    {
        // The dictionary is a value rather than a default, as its CoreProperty form had no default.
        Variables.Value = [];
        ScanProperties<Project>();
    }

    // Serialize and Deserialize write and read the items and variables themselves.
    [NotAutoSerialized]
    public IListProperty<ProjectItem> Items { get; } = new ListProperty<ProjectItem> { AffectsRender = false };

    [NotAutoSerialized]
    public IProperty<Dictionary<string, string>> Variables { get; } = Property.CreateValue<Dictionary<string, string>>(null!);

    // A whole number such as 30 or a fraction such as 30000/1001.
    public IProperty<Rational> FrameRate { get; } = Property.CreateValue(new Rational(30));

    public IProperty<int> SampleRate { get; } = Property.CreateValue(44100);

    public IProperty<SpeakerLayout> ChannelLayout { get; } = Property.CreateValue(SpeakerLayout.Stereo);

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

    // The rate GetFrameRate gives: the project's when it is positive, or 30.
    internal static Rational ReadFrameRate(Project? project)
        => project?.FrameRate.Value is { Numerator: > 0, Denominator: > 0 } rate ? rate : new Rational(30);

    public override void Deserialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Deserialize");
        base.Deserialize(context);

        // The variables and rates come first: the items are read on the project's frame grid.
        if (context.GetValue<Dictionary<string, string>>("variables") is { } vars)
        {
            Variables.Value.Clear();
            foreach (KeyValuePair<string, string> item in vars)
            {
                Variables.Value.Add(item.Key, item.Value);
            }
        }

        // Earlier versions kept the rates as variables, which a read moves to the rate members.
        if (Variables.Value.Remove(ProjectVariableKeys.FrameRate, out string? frameRate) && !context.Contains(nameof(FrameRate))
            && Rational.TryParse(frameRate, CultureInfo.InvariantCulture, out Rational rate))
        {
            FrameRate.Value = rate;
        }

        if (Variables.Value.Remove(ProjectVariableKeys.SampleRate, out string? sampleRate) && !context.Contains(nameof(SampleRate))
            && int.TryParse(sampleRate, NumberStyles.Integer, CultureInfo.InvariantCulture, out int samples))
        {
            SampleRate.Value = samples;
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

        context.SetValue("variables", Variables.Value);
    }
}
