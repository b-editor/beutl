using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media.Encoding;

public class VideoEncoderSettings : MediaEncoderSettings
{
    public VideoEncoderSettings()
        : this(5_000_000, 12)
    {
    }

    // A derived type passes its own defaults for the bit rate and the key frame rate.
    protected VideoEncoderSettings(int defaultBitrate, int defaultKeyframeRate)
    {
        BitrateProperty = new ValueOnlyProperty<int>(defaultBitrate);
        KeyframeRateProperty = new ValueOnlyProperty<int>(defaultKeyframeRate);
        ScanProperties<VideoEncoderSettings>();
    }

    [Browsable(false)]
    public IProperty<PixelSize> SourceSizeProperty { get; } = new ValueOnlyProperty<PixelSize>(default);

    [Display(Name = nameof(Strings.FrameSize), Description = nameof(Strings.FrameSize_Tip), ResourceType = typeof(Strings), Order = int.MinValue)]
    public IProperty<PixelSize> DestinationSizeProperty { get; } = new ValueOnlyProperty<PixelSize>(default);

    [Display(Name = nameof(Strings.FrameRate), Description = nameof(Strings.FrameRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 1)]
    public IProperty<Rational> FrameRateProperty { get; } = new ValueOnlyProperty<Rational>(new(30, 1));

    [Display(Name = nameof(Strings.Bitrate), Description = nameof(Strings.Bitrate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 2)]
    public IProperty<int> BitrateProperty { get; }

    [Display(Name = nameof(Strings.KeyframeRate), Description = nameof(Strings.KeyframeRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 3)]
    public IProperty<int> KeyframeRateProperty { get; }

    public PixelSize SourceSize
    {
        get => SourceSizeProperty.Value;
        set => SourceSizeProperty.Value = value;
    }

    public PixelSize DestinationSize
    {
        get => DestinationSizeProperty.Value;
        set => DestinationSizeProperty.Value = value;
    }

    public Rational FrameRate
    {
        get => FrameRateProperty.Value;
        set => FrameRateProperty.Value = value;
    }

    public int Bitrate
    {
        get => BitrateProperty.Value;
        set => BitrateProperty.Value = value;
    }

    public int KeyframeRate
    {
        get => KeyframeRateProperty.Value;
        set => KeyframeRateProperty.Value = value;
    }
}

