using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using Beutl.Converters;

namespace Beutl.Audio;

public enum AudioChannelFormat
{
    Auto,
    Mono,
    Stereo,
    Surround51,
    Surround71
}

[JsonConverter(typeof(AudioChannelMapJsonConverter))]
public sealed class AudioChannelMap : IEquatable<AudioChannelMap>
{
    public static readonly AudioChannelMap Auto = new(AudioChannelFormat.Auto, default, []);

    public AudioChannelMap(AudioChannelFormat format, AudioChannelLayout sourceLayout, IEnumerable<int> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));

        Sources = sources.ToImmutableArray();
        if (format == AudioChannelFormat.Auto)
        {
            if (!Sources.IsEmpty)
                throw new ArgumentException("Auto has no channel assignments.", nameof(sources));
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLayout.ChannelCount, nameof(sourceLayout));
            if (Sources.Length != GetFormatLayout(format).ChannelCount)
                throw new ArgumentException("Each clip channel must have one source assignment.", nameof(sources));
            if (Sources.Any(channel => channel < -1 || channel >= sourceLayout.ChannelCount))
                throw new ArgumentOutOfRangeException(nameof(sources), "Source channels must be -1 or within the source layout.");
            SourceLayout = sourceLayout;
        }

        Format = format;
    }

    public AudioChannelFormat Format { get; }

    public ImmutableArray<int> Sources { get; }

    public AudioChannelLayout SourceLayout { get; }

    public AudioChannelMap WithSources(IEnumerable<int> sources) => new(Format, SourceLayout, sources);

    public AudioChannelMap Resolve(AudioChannelLayout actualSourceLayout)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actualSourceLayout.ChannelCount, nameof(actualSourceLayout));
        return Format != AudioChannelFormat.Auto && SourceLayout == actualSourceLayout ? this : Auto;
    }

    public AudioChannelLayout ResolveClipLayout(AudioChannelLayout actualSourceLayout)
        => Resolve(actualSourceLayout).Format is AudioChannelFormat.Auto
            ? actualSourceLayout : GetFormatLayout(Format);

    public static AudioChannelMap CreateDefault(AudioChannelFormat format, AudioChannelLayout sourceLayout)
    {
        if (format == AudioChannelFormat.Auto) return Auto;

        AudioChannelLayout clipLayout = GetFormatLayout(format);
        int[] sources = new int[clipLayout.ChannelCount];
        for (int i = 0; i < sources.Length; i++)
        {
            int source = format is AudioChannelFormat.Surround51 or AudioChannelFormat.Surround71
                ? sourceLayout.IndexOf(clipLayout.GetSpeaker(i)) : -1;
            sources[i] = source >= 0 ? source : i < sourceLayout.ChannelCount ? i : -1;
        }
        return new AudioChannelMap(format, sourceLayout, sources);
    }

    public static AudioChannelLayout GetFormatLayout(AudioChannelFormat format) => format switch
    {
        AudioChannelFormat.Mono => AudioChannelLayout.Mono,
        AudioChannelFormat.Stereo => AudioChannelLayout.Stereo,
        AudioChannelFormat.Surround51 => AudioChannelLayout.Surround51,
        AudioChannelFormat.Surround71 => AudioChannelLayout.Surround71,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string GetChannelName(AudioChannelLayout layout, int channel) => layout.GetSpeaker(channel) switch
    {
        AudioSpeakers.FrontLeft => "L",
        AudioSpeakers.FrontRight => "R",
        AudioSpeakers.FrontCenter => "C",
        AudioSpeakers.LowFrequency => "LFE",
        AudioSpeakers.BackLeft => (layout.Speakers & AudioSpeakers.SideLeft) != 0 ? "Lrs" : "Ls",
        AudioSpeakers.BackRight => (layout.Speakers & AudioSpeakers.SideRight) != 0 ? "Rrs" : "Rs",
        AudioSpeakers.FrontLeftOfCenter => "Lc",
        AudioSpeakers.FrontRightOfCenter => "Rc",
        AudioSpeakers.BackCenter => "Cs",
        AudioSpeakers.SideLeft => "Ls",
        AudioSpeakers.SideRight => "Rs",
        AudioSpeakers.TopCenter => "Tc",
        AudioSpeakers.TopFrontLeft => "Tfl",
        AudioSpeakers.TopFrontCenter => "Tfc",
        AudioSpeakers.TopFrontRight => "Tfr",
        AudioSpeakers.TopBackLeft => "Tbl",
        AudioSpeakers.TopBackCenter => "Tbc",
        AudioSpeakers.TopBackRight => "Tbr",
        _ => (channel + 1).ToString(CultureInfo.InvariantCulture)
    };

    public static string DescribeSourceChannel(AudioChannelLayout layout, int channel)
    {
        string name = GetChannelName(layout, channel);
        return layout.HasPositions ? $"{channel + 1} ({name})" : name;
    }

    public bool Equals(AudioChannelMap? other)
        => ReferenceEquals(this, other) || other is not null
            && Format == other.Format && SourceLayout == other.SourceLayout
            && Sources.AsSpan().SequenceEqual(other.Sources.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as AudioChannelMap);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Format);
        hash.Add(SourceLayout);
        foreach (int source in Sources) hash.Add(source);
        return hash.ToHashCode();
    }
}
