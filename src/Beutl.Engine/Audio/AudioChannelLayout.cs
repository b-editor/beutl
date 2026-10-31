using System.Numerics;

namespace Beutl.Audio;

/// <summary>
/// Channels ordered by ascending speaker-mask bit. A mask of <see cref="AudioSpeakers.None"/>
/// describes discrete channels with no known positions.
/// </summary>
public readonly record struct AudioChannelLayout
{
    public AudioChannelLayout(int channelCount, AudioSpeakers speakers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        if (speakers != AudioSpeakers.None && BitOperations.PopCount((uint)speakers) != channelCount)
            throw new ArgumentException("The speaker count must match the channel count.", nameof(speakers));

        ChannelCount = channelCount;
        Speakers = speakers;
    }

    public int ChannelCount { get; }

    public AudioSpeakers Speakers { get; }

    public bool HasPositions => Speakers != AudioSpeakers.None;

    public static AudioChannelLayout Mono { get; } = new(1, AudioSpeakers.FrontCenter);

    public static AudioChannelLayout Stereo { get; } = new(2, AudioSpeakers.FrontLeft | AudioSpeakers.FrontRight);

    public static AudioChannelLayout Surround51 { get; } = new(6,
        Stereo.Speakers | AudioSpeakers.FrontCenter | AudioSpeakers.LowFrequency | AudioSpeakers.BackLeft | AudioSpeakers.BackRight);

    public static AudioChannelLayout Surround71 { get; } = new(8,
        Surround51.Speakers | AudioSpeakers.SideLeft | AudioSpeakers.SideRight);

    public AudioSpeakers GetSpeaker(int channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ChannelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, ChannelCount);
        uint mask = (uint)Speakers;
        for (int i = 0; i < channel && mask != 0; i++)
            mask &= mask - 1;
        return (AudioSpeakers)(mask & (0u - mask));
    }

    public int IndexOf(AudioSpeakers speaker)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ChannelCount);
        uint bit = (uint)speaker;
        if (!BitOperations.IsPow2(bit) || (Speakers & speaker) == 0)
            return -1;
        return BitOperations.PopCount((uint)Speakers & (bit - 1));
    }

    public static AudioChannelLayout Discrete(int count) => new(count, AudioSpeakers.None);

    /// <summary>Uses the declared layout when its count matches the buffer, otherwise Beutl's default order.</summary>
    public static AudioChannelLayout ForChannelCount(AudioChannelLayout declared, int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(declared.ChannelCount, nameof(declared));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        return declared.ChannelCount == channelCount ? declared : GetDefault(channelCount);
    }

    /// <summary>Beutl's assumed speaker order when a source does not declare positions.</summary>
    public static AudioChannelLayout GetDefault(int count) => count switch
    {
        1 => Mono,
        2 => Stereo,
        3 => new(3, Stereo.Speakers | AudioSpeakers.FrontCenter),
        4 => new(4, Stereo.Speakers | AudioSpeakers.BackLeft | AudioSpeakers.BackRight),
        5 => new(5, Stereo.Speakers | AudioSpeakers.FrontCenter | AudioSpeakers.BackLeft | AudioSpeakers.BackRight),
        6 => Surround51,
        7 => new(7, Stereo.Speakers | AudioSpeakers.FrontCenter | AudioSpeakers.LowFrequency
            | AudioSpeakers.BackCenter | AudioSpeakers.SideLeft | AudioSpeakers.SideRight),
        8 => Surround71,
        _ => Discrete(count)
    };
}
