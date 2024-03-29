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

    public int SampleRate
    {
        get => GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    public int Channels
    {
        get => GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    public int Bitrate
    {
        get => GetValue(BitrateProperty);
        set => SetValue(BitrateProperty, value);
    }
}

