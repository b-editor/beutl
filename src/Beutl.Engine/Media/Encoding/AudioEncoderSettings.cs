using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media.Encoding;

public class AudioEncoderSettings : MediaEncoderSettings
{
    public AudioEncoderSettings()
        : this(128_000)
    {
    }

    // A derived type passes its own default for the bit rate.
    protected AudioEncoderSettings(int defaultBitrate)
    {
        BitrateProperty = new ValueOnlyProperty<int>(defaultBitrate);
        ScanProperties<AudioEncoderSettings>();
    }

    [Display(Name = nameof(Strings.SampleRate), Description = nameof(Strings.SampleRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue)]
    public IProperty<int> SampleRateProperty { get; } = new ValueOnlyProperty<int>(44100);

    [Display(Name = nameof(Strings.Channels), Description = nameof(Strings.Channels_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 1)]
    public IProperty<int> ChannelsProperty { get; } = new ValueOnlyProperty<int>(2);

    [Display(Name = nameof(Strings.Bitrate), Description = nameof(Strings.Bitrate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 2)]
    public IProperty<int> BitrateProperty { get; }

    public int SampleRate
    {
        get => SampleRateProperty.Value;
        set => SampleRateProperty.Value = value;
    }

    public int Channels
    {
        get => ChannelsProperty.Value;
        set => ChannelsProperty.Value = value;
    }

    public int Bitrate
    {
        get => BitrateProperty.Value;
        set => BitrateProperty.Value = value;
    }
}

