using Beutl.Language;
using System.ComponentModel.DataAnnotations;

namespace Beutl.Media.Encoding;

public class AudioEncoderSettings : MediaEncoderSettings
{
    public static readonly CoreProperty<int> SampleRateProperty;
    public static readonly CoreProperty<int> ChannelsProperty;
    public static readonly CoreProperty<int> BitrateProperty;

    static AudioEncoderSettings()
    {
        SampleRateProperty = ConfigureProperty<int, AudioEncoderSettings>(nameof(SampleRate))
            .DefaultValue(44100)
            .Register();

        ChannelsProperty = ConfigureProperty<int, AudioEncoderSettings>(nameof(Channels))
            .DefaultValue(2)
            .Register();

        BitrateProperty = ConfigureProperty<int, AudioEncoderSettings>(nameof(Bitrate))
            .DefaultValue(128_000)
            .Register();
    }

    [Display(Name = nameof(Strings.SampleRate), Description = nameof(Strings.SampleRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue)]
    public int SampleRate
    {
        get => GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    [Display(Name = nameof(Strings.Channels), Description = nameof(Strings.Channels_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 1)]
    public int Channels
    {
        get => GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    [Display(Name = nameof(Strings.Bitrate), Description = nameof(Strings.Bitrate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 2)]
    public int Bitrate
    {
        get => GetValue(BitrateProperty);
        set => SetValue(BitrateProperty, value);
    }
}

