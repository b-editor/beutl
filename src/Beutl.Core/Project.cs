using System.Diagnostics;
using System.Globalization;
using Beutl.JsonConverters;
using Beutl.Serialization;

namespace Beutl;

// The variables that held the rates before 2.0.0-preview.9, which a project reads once and passes to its scenes.
internal static class ProjectVariableKeys
{
    public const string FrameRate = "framerate";
    public const string SampleRate = "samplerate";
}

internal readonly record struct LegacyProjectRates(
    Rational? FrameRate,
    int? SampleRate,
    SpeakerLayout? ChannelLayout);

// Development builds since #4110 kept the project's color palettes in the project file under ColorPalettes. They are
// in a file of their own now (ProjectColorPaletteFile), and a project drops that key rather than keeping it as an
// unknown member.
[DroppedMembers("ColorPalettes")]
public sealed partial class Project : Hierarchical
{
    private const string LegacyFrameRateKey = "FrameRate";
    private const string LegacySampleRateKey = "SampleRate";
    private const string LegacyChannelLayoutKey = "ChannelLayout";

    private static readonly AsyncLocal<LegacyProjectRates?> s_legacyRates = new();
    private LegacyProjectRates? _unpersistedLegacyRates;

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

    internal static LegacyProjectRates? CurrentLegacyRates => s_legacyRates.Value;

    private static LegacyRatesScope PushLegacyRates(LegacyProjectRates? rates)
    {
        LegacyProjectRates? previous = s_legacyRates.Value;
        s_legacyRates.Value = rates;
        return new LegacyRatesScope(previous);
    }

    private readonly struct LegacyRatesScope(LegacyProjectRates? previous) : IDisposable
    {
        public void Dispose()
        {
            s_legacyRates.Value = previous;
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Project.Deserialize");
        base.Deserialize(context);

        if (context.GetValue<Dictionary<string, string>>("variables") is { } vars)
        {
            Variables.Value.Clear();
            foreach (KeyValuePair<string, string> item in vars)
            {
                Variables.Value.Add(item.Key, item.Value);
            }
        }

        // Earlier versions kept audio/video settings on the project (as variables before 2.0.0-preview.9,
        // then as project members before 2.0.0-preview.16). Read them once and pass them to the scenes
        // restored below so scenes that do not record their own settings inherit them.
        Rational? legacyFrameRate = null;
        int? legacySampleRate = null;
        SpeakerLayout? legacyChannelLayout = null;

        bool hasFrameRateMember = context.Contains(LegacyFrameRateKey);
        if (hasFrameRateMember)
        {
            Optional<Rational> value = context.GetValue<Optional<Rational>>(LegacyFrameRateKey);
            if (value.HasValue)
            {
                legacyFrameRate = value.Value;
            }
        }

        bool hasSampleRateMember = context.Contains(LegacySampleRateKey);
        if (hasSampleRateMember)
        {
            Optional<int> value = context.GetValue<Optional<int>>(LegacySampleRateKey);
            if (value.HasValue)
            {
                legacySampleRate = value.Value;
            }
        }

        if (context.Contains(LegacyChannelLayoutKey))
        {
            Optional<SpeakerLayout> value = context.GetValue<Optional<SpeakerLayout>>(LegacyChannelLayoutKey);
            if (value.HasValue)
            {
                legacyChannelLayout = value.Value;
            }
        }

        if (Variables.Value.Remove(ProjectVariableKeys.FrameRate, out string? frameRate) && !hasFrameRateMember
            && Rational.TryParse(frameRate, CultureInfo.InvariantCulture, out Rational rate))
        {
            legacyFrameRate = rate;
        }

        if (Variables.Value.Remove(ProjectVariableKeys.SampleRate, out string? sampleRate) && !hasSampleRateMember
            && int.TryParse(sampleRate, NumberStyles.Integer, CultureInfo.InvariantCulture, out int samples))
        {
            legacySampleRate = samples;
        }

        LegacyProjectRates? legacyRates = legacyFrameRate.HasValue || legacySampleRate.HasValue || legacyChannelLayout.HasValue
            ? new LegacyProjectRates(legacyFrameRate, legacySampleRate, legacyChannelLayout)
            : null;

        using (PushLegacyRates(legacyRates))
        using (MediaTimeJsonConverter.ReadOnFrameGrid(
            IFrameRateSource.Normalize(legacyFrameRate ?? IFrameRateSource.DefaultFrameRate)))
        {
            if (context.GetValue<ProjectItem[]>("items") is { } items)
            {
                Items.Replace(items);
            }
        }

        _unpersistedLegacyRates = legacyRates.HasValue && Items.Any(static item => item.HasUnpersistedLegacyRates)
            ? legacyRates
            : null;

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

        // When saving only the project file (such as an auto-save after editing one scene), retain
        // legacy project-level rates until every migrated scene has been persisted to its own file.
        if (_unpersistedLegacyRates is { } legacy
            && !context.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects)
            && !context.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects))
        {
            if (Items.Any(static item => item.HasUnpersistedLegacyRates))
            {
                if (legacy.FrameRate is { } frameRate)
                    context.SetValue(LegacyFrameRateKey, frameRate);
                if (legacy.SampleRate is { } sampleRate)
                    context.SetValue(LegacySampleRateKey, sampleRate);
                if (legacy.ChannelLayout is { } channelLayout)
                    context.SetValue(LegacyChannelLayoutKey, channelLayout);
            }
            else
            {
                _unpersistedLegacyRates = null;
            }
        }

        context.SetValue("items", Items);

        context.SetValue("variables", Variables.Value);
    }

    internal void ClearUnpersistedLegacyRatesIfPersisted()
    {
        if (_unpersistedLegacyRates.HasValue && !Items.Any(static item => item.HasUnpersistedLegacyRates))
        {
            _unpersistedLegacyRates = null;
        }
    }
}
