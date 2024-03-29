using System.ComponentModel;

namespace Beutl.Media.Encoding;

public class VideoEncoderSettings : MediaEncoderSettings
{
    public static readonly CoreProperty<PixelSize> SourceSizeProperty;
    public static readonly CoreProperty<PixelSize> DestinationSizeProperty;
    public static readonly CoreProperty<Rational> FrameRateProperty;
    public static readonly CoreProperty<int> BitrateProperty;
    public static readonly CoreProperty<int> KeyframeRateProperty;

    static VideoEncoderSettings()
    {
        SourceSizeProperty = ConfigureProperty<PixelSize, VideoEncoderSettings>(nameof(SourceSize))
            .Register();

        DestinationSizeProperty = ConfigureProperty<PixelSize, VideoEncoderSettings>(nameof(DestinationSize))
            .Register();

        FrameRateProperty = ConfigureProperty<Rational, VideoEncoderSettings>(nameof(FrameRate))
            .DefaultValue(new(30, 1))
            .Register();

        BitrateProperty = ConfigureProperty<int, VideoEncoderSettings>(nameof(Bitrate))
            .DefaultValue(5_000_000)
            .Register();

        KeyframeRateProperty = ConfigureProperty<int, VideoEncoderSettings>(nameof(KeyframeRate))
            .DefaultValue(12)
            .Register();
    }

    [Browsable(false)]
    public PixelSize SourceSize
    {
        get => GetValue(SourceSizeProperty);
        set => SetValue(SourceSizeProperty, value);
    }

    public PixelSize DestinationSize
    {
        get => GetValue(DestinationSizeProperty);
        set => SetValue(DestinationSizeProperty, value); 
    }

    public Rational FrameRate
    {
        get => GetValue(FrameRateProperty);
        set => SetValue(FrameRateProperty, value); 
    }

    public int Bitrate
    {
        get => GetValue(BitrateProperty);
        set => SetValue(BitrateProperty, value); 
    }

    public int KeyframeRate
    {
        get => GetValue(KeyframeRateProperty);
        set => SetValue(KeyframeRateProperty, value); 
    }
}

