using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Language;

namespace Beutl.Media.Encoding;

public partial class AudioEncoderSettings : MediaEncoderSettings
{
    public AudioEncoderSettings()
        : this(128_000)
    {
    }

    // A derived type passes its own default for the bit rate.
    protected AudioEncoderSettings(int defaultBitrate)
    {
        Bitrate = Property.CreateValue(defaultBitrate);
        ScanProperties<AudioEncoderSettings>();
    }

    /// <summary>The speaker layout written by an export with these settings.</summary>
    public AudioChannelLayout GetChannelLayout() => AudioChannelLayout.GetDefault(Channels.Value);

    /// <summary>Changes the channel count, keeping a positive total bitrate proportional to it.</summary>
    /// <remarks>Use for edits and new exports, not when restoring or copying saved settings.</remarks>
    public void SetChannels(int channels)
    {
        int previousChannels = Channels.Value;
        if (previousChannels > 0 && channels > 0 && Bitrate.Value > 0)
        {
            Bitrate.Value = (int)Math.Round((double)Bitrate.Value * channels / previousChannels,
                MidpointRounding.AwayFromZero);
        }

        Channels.Value = channels;
    }

    [Display(Name = nameof(Strings.SampleRate), Description = nameof(Strings.SampleRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue)]
    public IProperty<int> SampleRate { get; } = Property.CreateValue(44100);

    [Display(Name = nameof(Strings.Channels), Description = nameof(Strings.Channels_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 1)]
    public IProperty<int> Channels { get; } = Property.CreateValue(2);

    [Display(Name = nameof(Strings.Bitrate), Description = nameof(Strings.Bitrate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 2)]
    public IProperty<int> Bitrate { get; }
}

